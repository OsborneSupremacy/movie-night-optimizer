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

            return match.IsFound
                ? match
                : await SearchAsync(token, typed, string.Empty, budget.Token).ConfigureAwait(false);
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
