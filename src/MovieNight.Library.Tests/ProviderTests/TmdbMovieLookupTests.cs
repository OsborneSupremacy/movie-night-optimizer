using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using static MovieNight.Library.Providers.TmdbMovieLookup;

namespace MovieNight.Library.Tests.ProviderTests;

/// <summary>
/// Which TMDB result is chosen, and that nothing TMDB or Secrets Manager does can fail a suggestion.
/// </summary>
public class TmdbMovieLookupTests
{
    private const string Token = "test-token";

    private static SearchResult Result(int id, string title, double popularity = 1, string releaseDate = "2000-01-01",
        string? posterPath = "/p.jpg", string? originalTitle = null) =>
        new()
        {
            Id = id,
            Title = title,
            OriginalTitle = originalTitle ?? title,
            ReleaseDate = releaseDate,
            PosterPath = posterPath,
            Popularity = popularity
        };

    /// <summary>The search requests, leaving out the details request a found movie also makes.</summary>
    private static IEnumerable<HttpRequestMessage> Searches(FakeHttpHandler handler) =>
        handler.Requests.Where(request => request.RequestUri!.AbsolutePath.EndsWith("/search/movie"));

    private static string Page(params object[] results) =>
        JsonSerializer.Serialize(new { page = 1, results });

    private static object Raw(int id, string title, string releaseDate, double popularity = 1, string? posterPath = "/p.jpg") =>
        new { id, title, original_title = title, release_date = releaseDate, poster_path = posterPath, popularity };

    private static (TmdbMovieLookup lookup, FakeHttpHandler handler) Build(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        string token = Token)
    {
        var handler = new FakeHttpHandler(respond);
        var tokenSource = Substitute.For<ITmdbTokenSource>();
        tokenSource.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(token);

        var lookup = new TmdbMovieLookup(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") },
            tokenSource,
            NullLogger<TmdbMovieLookup>.Instance);

        return (lookup, handler);
    }

    [Fact]
    public void Choose_AnExactTitle_BeatsAMoreRelevantResult() =>
        Choose("Alien", [Result(1, "Aliens", 90), Result(2, "Alien", 50)]).TmdbId.Should().Be(2);

    [Fact]
    public void Choose_SeveralExactTitles_TakesTheMostPopular() =>
        Choose("Alien", [Result(1, "Alien", 2), Result(348, "Alien", 80, "1979-05-25")])
            .Should().BeEquivalentTo(new MovieMatch { TmdbId = 348, Title = "Alien", Year = "1979", PosterPath = "/p.jpg" });

    [Fact]
    public void Choose_NoExactTitle_TakesTmdbsFirstResult() =>
        Choose("Paddington Two", [Result(346648, "Paddington 2", 30), Result(116149, "Paddington", 40)])
            .TmdbId.Should().Be(346648);

    [Fact]
    public void Choose_MatchesTheOriginalTitleToo() =>
        Choose("Le Fabuleux Destin d'Amélie Poulain",
                [Result(1, "Something Else", 90), Result(194, "Amélie", 40, originalTitle: "Le Fabuleux Destin d'Amélie Poulain")])
            .TmdbId.Should().Be(194);

    [Fact]
    public void Choose_NoResults_IsNone() =>
        Choose("asdfghjkl", []).Should().Be(MovieMatch.None);

    [Fact]
    public void Choose_MissingYearAndPoster_AreEmptyNotNull() =>
        Choose("Alien", [Result(1, "Alien", releaseDate: "", posterPath: null)])
            .Should().BeEquivalentTo(new MovieMatch { TmdbId = 1, Title = "Alien", Year = "", PosterPath = "" });

    [Theory]
    [InlineData("Alien 1979", "Alien", "1979")]
    [InlineData("Alien (1979)", "Alien", "1979")]
    [InlineData("Dune, 1984", "Dune", "1984")]
    [InlineData("Blade Runner 2049", "Blade Runner", "2049")]
    [InlineData("1917", "1917", "")]
    [InlineData("2001: A Space Odyssey", "2001: A Space Odyssey", "")]
    [InlineData("Alien", "Alien", "")]
    public void SplitYear_FindsATrailingYear(string typed, string title, string year) =>
        SplitYear(typed).Should().Be((title, year));

    [Fact]
    public async Task FindAsync_SendsTheTokenAndAsksForNoAdultResults()
    {
        var (lookup, handler) = Build((_, _) => Task.FromResult(FakeHttpHandler.Json(Page(Raw(348, "Alien", "1979-05-25")))));

        var match = await lookup.FindAsync("Alien");

        match.TmdbId.Should().Be(348);
        var request = Searches(handler).Single();
        request.Headers.Authorization!.ToString().Should().Be($"Bearer {Token}");
        request.RequestUri!.Query.Should().Contain("include_adult=false").And.Contain("query=Alien");
    }

    [Fact]
    public async Task FindAsync_AYearThatMatchesNothing_SearchesTheWholeTitle()
    {
        // "Blade Runner 2049" first asks for Blade Runner from 2049, finds nothing, and tries again.
        var (lookup, handler) = Build((request, _) => Task.FromResult(FakeHttpHandler.Json(
            request.RequestUri!.Query.Contains("primary_release_year=2049")
                ? Page()
                : Page(Raw(335984, "Blade Runner 2049", "2017-10-04")))));

        var match = await lookup.FindAsync("Blade Runner 2049");

        match.TmdbId.Should().Be(335984);
        Searches(handler).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FindAsync_TmdbAnswersWithAnError_IsNoneAndDoesNotRetry(HttpStatusCode status)
    {
        var (lookup, handler) = Build((_, _) => Task.FromResult(FakeHttpHandler.Json("{}", status)));

        (await lookup.FindAsync("Alien")).Should().Be(MovieMatch.None);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task FindAsync_TmdbIsUnreachable_IsNone()
    {
        var (lookup, _) = Build((_, _) => throw new HttpRequestException("connection refused"));

        (await lookup.FindAsync("Alien")).Should().Be(MovieMatch.None);
    }

    [Fact]
    public async Task FindAsync_TmdbIsSlow_GivesUpWithinTheBudget()
    {
        var (lookup, _) = Build(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return FakeHttpHandler.Json(Page(Raw(348, "Alien", "1979-05-25")));
        });

        var started = DateTimeOffset.UtcNow;
        var match = await lookup.FindAsync("Alien");

        match.Should().Be(MovieMatch.None);
        (DateTimeOffset.UtcNow - started).Should().BeLessThan(Budget + TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"results\": null}")]
    [InlineData("{\"results\": [{\"id\": 0}]}")]
    public async Task FindAsync_AnAnswerItCantUse_IsNone(string body)
    {
        var (lookup, _) = Build((_, _) => Task.FromResult(FakeHttpHandler.Json(body)));

        (await lookup.FindAsync("Alien")).Should().Be(MovieMatch.None);
    }

    [Fact]
    public async Task FindAsync_NoToken_DoesNotCallTmdb()
    {
        var (lookup, handler) = Build((_, _) => Task.FromResult(FakeHttpHandler.Json(Page())), token: "");

        (await lookup.FindAsync("Alien")).Should().Be(MovieMatch.None);
        handler.Requests.Should().BeEmpty();
    }

    private const string PaddingtonDetails = """
        {
          "id": 346648,
          "runtime": 104,
          "genres": [ { "id": 12, "name": "Adventure" }, { "id": 35, "name": "Comedy" }, { "id": 10751, "name": "Family" }, { "id": 1, "name": "Fourth" } ],
          "release_dates": { "results": [
            { "iso_3166_1": "GB", "release_dates": [ { "type": 3, "certification": "PG" } ] },
            { "iso_3166_1": "US", "release_dates": [ { "type": 5, "certification": "G" }, { "type": 3, "certification": "PG" } ] }
          ] },
          "watch/providers": { "results": { "US": {
            "flatrate": [
              { "provider_name": "HBO Max Amazon Channel", "display_priority": 14 },
              { "provider_name": "HBO Max", "display_priority": 154 },
              { "provider_name": "Netflix Standard with Ads", "display_priority": 20 }
            ],
            "rent": [ { "provider_name": "Apple TV", "display_priority": 2 } ]
          } } },
          "videos": { "results": [
            { "site": "YouTube", "type": "Teaser", "official": true, "key": "teaser" },
            { "site": "YouTube", "type": "Trailer", "official": false, "key": "fan" },
            { "site": "YouTube", "type": "Trailer", "official": true, "key": "official" }
          ] }
        }
        """;

    [Fact]
    public void ParseDetails_ReadsEveryPart()
    {
        var details = ParseDetails(PaddingtonDetails);

        details.Certification.Should().Be("PG", "the US theatrical rating beats the US home release's");
        details.RuntimeMinutes.Should().Be(104);
        details.Genres.Should().Equal("Adventure", "Comedy", "Family");
        details.StreamingOn.Should().Equal(["HBO Max", "Netflix"], "channel and ad-plan variants are the same service, and rentals don't count");
        details.TrailerUrl.Should().Be("https://www.youtube.com/watch?v=official");
    }

    [Fact]
    public void ParseDetails_MissingOrMalformedParts_LeaveOnlyThemselvesOut()
    {
        const string json = """
            { "runtime": null, "genres": "not a list", "release_dates": { "results": [] },
              "watch/providers": { "results": { "GB": { "flatrate": [ { "provider_name": "BBC iPlayer" } ] } } },
              "videos": { "results": [ { "site": "Vimeo", "type": "Trailer", "key": "x" } ] } }
            """;

        ParseDetails(json).Should().BeEquivalentTo(MovieDetails.Empty);
    }

    [Fact]
    public void ParseDetails_NoAppendedParts_IsJustTheBasics()
    {
        var details = ParseDetails("""{ "runtime": 88, "genres": [ { "name": "Animation" } ] }""");

        details.RuntimeMinutes.Should().Be(88);
        details.Genres.Should().Equal("Animation");
        details.Certification.Should().BeEmpty();
        details.StreamingOn.Should().BeEmpty();
    }

    [Theory]
    [InlineData("HBO Max Amazon Channel", "HBO Max")]
    [InlineData("Starz Apple TV Channel", "Starz")]
    [InlineData("Paramount+ Roku Premium Channel", "Paramount+")]
    [InlineData("Netflix Standard with Ads", "Netflix")]
    [InlineData("Netflix basic with Ads", "Netflix")]
    [InlineData("Disney Plus", "Disney Plus")]
    public void ServiceName_DropsChannelAndPlanSuffixes(string providerName, string expected) =>
        ServiceName(providerName).Should().Be(expected);

    [Fact]
    public async Task FindAsync_AFoundMovie_GetsItsDetails()
    {
        var (lookup, handler) = Build((request, _) => Task.FromResult(FakeHttpHandler.Json(
            request.RequestUri!.AbsolutePath.EndsWith("/movie/346648")
                ? PaddingtonDetails
                : Page(Raw(346648, "Paddington 2", "2017-11-10")))));

        var match = await lookup.FindAsync("Paddington 2");

        match.Details.Certification.Should().Be("PG");
        handler.Requests.Last().RequestUri!.Query.Should().Contain("append_to_response=release_dates,watch/providers,videos");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FindAsync_DetailsFail_KeepsTheMatchWithoutThem(HttpStatusCode status)
    {
        var (lookup, _) = Build((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/movie/348")
                ? FakeHttpHandler.Json("{}", status)
                : FakeHttpHandler.Json(Page(Raw(348, "Alien", "1979-05-25")))));

        var match = await lookup.FindAsync("Alien");

        match.TmdbId.Should().Be(348);
        match.Year.Should().Be("1979");
        match.Details.Should().BeEquivalentTo(MovieDetails.Empty);
    }

    [Fact]
    public async Task FindAsync_DetailsAreGarbled_KeepsTheMatchWithoutThem()
    {
        var (lookup, _) = Build((request, _) => Task.FromResult(FakeHttpHandler.Json(
            request.RequestUri!.AbsolutePath.EndsWith("/movie/348") ? "not json" : Page(Raw(348, "Alien", "1979-05-25")))));

        var match = await lookup.FindAsync("Alien");

        match.TmdbId.Should().Be(348);
        match.Details.Should().BeEquivalentTo(MovieDetails.Empty);
    }

    [Fact]
    public async Task TokenSource_ReadsTheSecretOnceAndKeepsIt()
    {
        Environment.SetEnvironmentVariable("TMDB_SECRET_ID", "movienight/tmdb");
        var secrets = Substitute.For<IAmazonSecretsManager>();
        secrets.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = "{\"apiReadAccessToken\":\"abc\",\"apiKey\":\"xyz\"}" });
        var source = new TmdbTokenSource(secrets, NullLogger<TmdbTokenSource>.Instance);

        (await source.GetTokenAsync(CancellationToken.None)).Should().Be("abc");
        (await source.GetTokenAsync(CancellationToken.None)).Should().Be("abc");

        await secrets.Received(1).GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TokenSource_AFailedRead_IsEmptyAndTriedAgainNextTime()
    {
        Environment.SetEnvironmentVariable("TMDB_SECRET_ID", "movienight/tmdb");
        var secrets = Substitute.For<IAmazonSecretsManager>();
        secrets.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new AmazonSecretsManagerException("throttled"),
                _ => Task.FromResult(new GetSecretValueResponse { SecretString = "{\"apiReadAccessToken\":\"abc\"}" }));
        var source = new TmdbTokenSource(secrets, NullLogger<TmdbTokenSource>.Instance);

        (await source.GetTokenAsync(CancellationToken.None)).Should().BeEmpty();
        (await source.GetTokenAsync(CancellationToken.None)).Should().Be("abc");
    }
}
