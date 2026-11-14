using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MovieNight.Library.Providers;

/// <summary>
/// Looks a typed title up with TMDB's movie search.
/// </summary>
/// <remarks>
/// Never throws and never keeps anybody waiting long. TMDB being down, slow, rate limiting, or
/// answering with something unreadable, the token missing, nothing matching: all of it is
/// <see cref="MovieMatch.None"/>, logged, and the suggestion is saved as it was typed. The whole
/// lookup, token included, gets <see cref="Budget"/>.
/// </remarks>
[UsedImplicitly]
internal partial class TmdbMovieLookup : IMovieLookup
{
    /// <summary>How long a lookup may take in all before the suggestion is saved without it.</summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(1.5);

    /// <summary>How many of TMDB's results are considered. Its first page is ordered by relevance.</summary>
    private const int CandidateCount = 10;

    private readonly HttpClient _httpClient;

    private readonly ITmdbTokenSource _tokenSource;

    private readonly ILogger<TmdbMovieLookup> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public TmdbMovieLookup(HttpClient httpClient, ITmdbTokenSource tokenSource, ILogger<TmdbMovieLookup> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MovieMatch> FindAsync(string typedTitle)
    {
        var typed = typedTitle.TrimNullSafe();

        if(typed.Length == 0)
            return MovieMatch.None;

        using var budget = new CancellationTokenSource(Budget);

        try
        {
            var token = await _tokenSource.GetTokenAsync(budget.Token).ConfigureAwait(false);

            if(token.Length == 0)
                return MovieMatch.None;

            // "Alien 1979" or "Alien (1979)" means Alien from 1979. If nothing came out that year,
            // the number may be part of the title, like Blade Runner 2049, so search it whole.
            var (title, year) = SplitYear(typed);

            var match = year.Length > 0
                ? await SearchAsync(token, title, year, budget.Token).ConfigureAwait(false)
                : MovieMatch.None;

            if(!match.IsFound)
                match = await SearchAsync(token, typed, string.Empty, budget.Token).ConfigureAwait(false);

            return match.IsFound
                ? match with { Details = await TryGetDetailsAsync(token, match.TmdbId, budget.Token).ConfigureAwait(false) }
                : match;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "TMDB lookup failed; saving the suggestion as typed");
            return MovieMatch.None;
        }
    }

    private async Task<MovieMatch> SearchAsync(string token, string query, string year, CancellationToken cancellationToken)
    {
        var path = $"search/movie?query={Uri.EscapeDataString(query)}&include_adult=false&language=en-US&page=1"
            + (year.Length > 0 ? $"&primary_release_year={year}" : string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if(!response.IsSuccessStatusCode)
        {
            // 429 is TMDB rate limiting; 401 is a bad token. Neither is worth a second try while
            // somebody waits on the button.
            _logger.LogWarning("TMDB search answered {StatusCode}; saving the suggestion as typed", (int)response.StatusCode);
            return MovieMatch.None;
        }

        var page = await JsonSerializer
            .DeserializeAsync<SearchPage>(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Choose(query, page?.Results ?? []);
    }

    /// <summary>
    /// The rating, runtime, genres, streaming services and trailer, in one request. Anything going
    /// wrong here keeps the match TMDB's search already found and leaves the details out.
    /// </summary>
    private async Task<MovieDetails> TryGetDetailsAsync(string token, int tmdbId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"movie/{tmdbId}?append_to_response=release_dates,watch/providers,videos&language=en-US");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if(!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB details answered {StatusCode}; keeping the match without them", (int)response.StatusCode);
                return MovieDetails.Empty;
            }

            return ParseDetails(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "TMDB details failed; keeping the match without them");
            return MovieDetails.Empty;
        }
    }

    /// <summary>
    /// The details from TMDB's movie response, reading each part on its own so that one missing or
    /// malformed part leaves only itself out.
    /// </summary>
    internal static MovieDetails ParseDetails(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new MovieDetails
        {
            Certification = Read(() => UsCertification(root), string.Empty),
            RuntimeMinutes = Read(() => root.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Number
                ? Math.Max(runtime.GetInt32(), 0)
                : 0, 0),
            Genres = Read(() => Strings(root, "genres", "name").Take(3).ToImmutableList(), []),
            StreamingOn = Read(() => UsStreaming(root), []),
            TrailerUrl = Read(() => Trailer(root), string.Empty)
        };
    }

    private static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// The US rating. The theatrical release's when it has one (type 3), otherwise any US release's,
    /// since a movie that went straight to streaming is rated there.
    /// </summary>
    private static string UsCertification(JsonElement root)
    {
        var us = Array(root.GetProperty("release_dates"), "results")
            .Where(country => Text(country, "iso_3166_1") == "US")
            .SelectMany(country => Array(country, "release_dates"))
            .Where(release => Text(release, "certification").Trim().Length > 0)
            .ToImmutableList();

        var theatrical = us.FirstOrDefault(release =>
            release.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Number && type.GetInt32() == 3);

        return theatrical.ValueKind == JsonValueKind.Object
            ? Text(theatrical, "certification").Trim()
            : us.Select(release => Text(release, "certification").Trim()).FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// US subscription and free services, in TMDB's display order, without the add-on channel
    /// variants that repeat a service ("HBO Max Amazon Channel" is HBO Max).
    /// </summary>
    private static ImmutableList<string> UsStreaming(JsonElement root)
    {
        if(!root.GetProperty("watch/providers").GetProperty("results").TryGetProperty("US", out var us))
            return [];

        return new[] { "flatrate", "free", "ads" }
            .SelectMany(kind => Array(us, kind))
            .OrderBy(provider => provider.TryGetProperty("display_priority", out var priority)
                && priority.ValueKind == JsonValueKind.Number ? priority.GetInt32() : int.MaxValue)
            .Select(provider => ServiceName(Text(provider, "provider_name")))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToImmutableList();
    }

    /// <summary>"HBO Max Amazon Channel" to "HBO Max", "Netflix Standard with Ads" to "Netflix".</summary>
    internal static string ServiceName(string providerName) =>
        ChannelOrPlanSuffix().Replace(providerName.Trim(), string.Empty).Trim();

    /// <summary>An official YouTube trailer if there is one, otherwise any YouTube trailer.</summary>
    private static string Trailer(JsonElement root)
    {
        var trailers = Array(root.GetProperty("videos"), "results")
            .Where(video => Text(video, "site") == "YouTube" && Text(video, "type") == "Trailer" && Text(video, "key").Length > 0)
            .ToImmutableList();

        var chosen = trailers.FirstOrDefault(video =>
            video.TryGetProperty("official", out var official) && official.ValueKind == JsonValueKind.True);

        if(chosen.ValueKind != JsonValueKind.Object)
            chosen = trailers.FirstOrDefault();

        return chosen.ValueKind == JsonValueKind.Object
            ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(Text(chosen, "key"))}"
            : string.Empty;
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static IEnumerable<string> Strings(JsonElement parent, string arrayName, string propertyName) =>
        Array(parent, arrayName)
            .Select(item => Text(item, propertyName).Trim())
            .Where(text => text.Length > 0);

    private static string Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// The best of TMDB's results for what was typed.
    /// </summary>
    /// <remarks>
    /// A title that matches exactly beats relevance, and among exact matches the most popular wins:
    /// "Alien" is the 1979 film, not one of the obscure ones that share its name. With no exact
    /// match, TMDB's own first result, which is how "Paddington Two" finds Paddington 2.
    /// </remarks>
    internal static MovieMatch Choose(string typed, IReadOnlyList<SearchResult> results)
    {
        var key = NightProgress.TitleKey(typed);

        var candidates = results
            .Where(result => result.Id > 0 && !string.IsNullOrWhiteSpace(result.Title))
            .Take(CandidateCount)
            .ToImmutableList();

        if(candidates.Count == 0)
            return MovieMatch.None;

        var chosen = candidates
            .Where(result => NightProgress.TitleKey(result.Title ?? string.Empty) == key
                || NightProgress.TitleKey(result.OriginalTitle ?? string.Empty) == key)
            .OrderByDescending(result => result.Popularity)
            .FirstOrDefault() ?? candidates[0];

        return new MovieMatch
        {
            TmdbId = chosen.Id,
            Title = (chosen.Title ?? string.Empty).Trim(),
            Year = YearOf(chosen.ReleaseDate ?? string.Empty),
            PosterPath = (chosen.PosterPath ?? string.Empty).Trim()
        };
    }

    /// <summary>"Alien (1979)" into "Alien" and "1979". A title that is only a year, like 1917, keeps it.</summary>
    internal static (string title, string year) SplitYear(string typed)
    {
        var match = TrailingYear().Match(typed);

        if(!match.Success || match.Groups["title"].Value.Trim().Length == 0)
            return (typed, string.Empty);

        return (match.Groups["title"].Value.Trim(), match.Groups["year"].Value);
    }

    private static string YearOf(string releaseDate) =>
        releaseDate.Length >= 4 && int.TryParse(releaseDate[..4], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? releaseDate[..4]
            : string.Empty;

    [GeneratedRegex(@"^(?<title>.*?)[\s,(\[]+(?<year>(?:18|19|20)\d{2})[)\]]?\s*$")]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"\s+(?:(?:Amazon|Apple TV|Roku Premium)\s+Channel|(?:Basic|Standard|Premium)?\s*with\s+Ads)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelOrPlanSuffix();

    // TMDB's shapes, nullable because its JSON can leave anything out. Read only by Choose, which
    // turns every null into a default before anything leaves this class.
    internal record SearchPage
    {
        [JsonPropertyName("results")]
        public ImmutableList<SearchResult>? Results { get; init; }
    }

    internal record SearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; init; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; init; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }

        [JsonPropertyName("popularity")]
        public double Popularity { get; init; }
    }
}
