namespace MovieNight.Library.Services;

/// <summary>
/// GET /api/movies/search?q=. The autocomplete list on the suggest page: a few movies TMDB thinks
/// could be what somebody is typing.
/// </summary>
/// <remarks>
/// Public, like TMDB's own data, so that answers can be shared. CloudFront caches a successful
/// answer for a day, keyed on q alone, and the browser does too: the same few letters typed by
/// anybody are answered at the edge without reaching this function, TMDB, or the API's rate limit.
/// A TMDB failure is answered with an empty list marked no-store, so an outage is never cached as
/// if nothing matched. The page treats an empty list and an error the same way: no suggestions,
/// and typing carries on.
/// </remarks>
[UsedImplicitly]
internal class MovieSearchService : IApiGatewayHandler
{
    /// <summary>Fewer letters than this match too much to be worth a request.</summary>
    internal const int MinQueryLength = 2;

    internal const string CacheForADay = "public, max-age=86400";

    private readonly ApiGatewayAdapter _adapter;

    private readonly IMovieLookup _movieLookup;

    public MovieSearchService(ApiGatewayAdapter adapter, IMovieLookup movieLookup)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _movieLookup = movieLookup ?? throw new ArgumentNullException(nameof(movieLookup));
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    )
    {
        var query = request.QueryStringParameters is not null && request.QueryStringParameters.TryGetValue("q", out var q)
            ? q.TrimNullSafe()
            : string.Empty;

        var (response, cacheControl) = await ExecuteAsync(query).ConfigureAwait(false);

        return ProxyResponseBuilder.Build(HttpStatusCode.OK, _adapter.Serialize(response), cacheControl);
    }

    internal async Task<(MovieSearchResponse response, string cacheControl)> ExecuteAsync(string query)
    {
        // Too short or too long to search is a fixed answer, and as cacheable as any other.
        if(query.Length < MinQueryLength || query.Length > NightProgress.MaxSuggestionLength)
            return (new MovieSearchResponse { Movies = [] }, CacheForADay);

        var search = await _movieLookup.SearchAsync(query).ConfigureAwait(false);

        var response = new MovieSearchResponse
        {
            Movies = search.Movies
                .Select(movie => new MovieOption
                {
                    TmdbId = movie.TmdbId,
                    Title = movie.Title,
                    Year = movie.Year,
                    PosterUrl = NightProgress.PosterUrl(movie.PosterPath, "w92")
                })
                .ToImmutableList()
        };

        return (response, search.Succeeded ? CacheForADay : "no-store");
    }
}
