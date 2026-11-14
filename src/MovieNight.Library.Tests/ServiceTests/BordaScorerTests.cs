namespace MovieNight.Library.Tests.ServiceTests;

public class BordaScorerTests
{
    private static Ballot BallotFrom(string voterId, params string[] rankedMovieIds) =>
        new()
        {
            VoterId = voterId,
            RankedMovieIds = [.. rankedMovieIds]
        };

    [Fact]
    public void Score_ThreePeopleRankingEachOthersMovies_OrdersByPoints()
    {
        // Each person suggested the movie with their own letter, and ranks the other two.
        string[] movies = ["a", "b", "c"];
        Ballot[] ballots =
        [
            BallotFrom("a", "b", "c"),
            BallotFrom("b", "c", "a"),
            BallotFrom("c", "b", "a")
        ];

        var scores = BordaScorer.Score(movies, ballots);

        scores.Select(score => score.MovieId).Should().Equal("b", "c", "a");
        scores.Select(score => score.Points).Should().Equal(4, 3, 2);
    }

    [Fact]
    public void Score_EqualPoints_GoesToTheMovieWithMoreFirstPlaceVotes()
    {
        // All three get four points: "a" and "c" as 3 + 1, "b" as 2 + 2. "a" and "c" were each
        // somebody's first choice and "b" was nobody's, so "b" comes last.
        string[] movies = ["a", "b", "c"];
        Ballot[] ballots =
        [
            BallotFrom("x", "a", "b", "c"),
            BallotFrom("y", "c", "b", "a")
        ];

        var scores = BordaScorer.Score(movies, ballots);

        scores[0].Points.Should().Be(scores[1].Points);
        scores[0].MovieId.Should().Be("a");
        scores[0].FirstPlaceVotes.Should().Be(1);
        scores[1].MovieId.Should().Be("c");
        scores[2].MovieId.Should().Be("b");
    }

    [Fact]
    public void Score_FullTie_IsOrderedByMovieIdSoTheResultIsRepeatable()
    {
        string[] movies = ["b", "a"];
        Ballot[] ballots =
        [
            BallotFrom("x", "a", "b"),
            BallotFrom("y", "b", "a")
        ];

        var scores = BordaScorer.Score(movies, ballots);

        scores.Select(score => score.MovieId).Should().Equal("a", "b");
    }

    [Fact]
    public void Score_MovieNobodyRanked_IsListedWithNoPoints()
    {
        string[] movies = ["a", "b"];
        Ballot[] ballots = [BallotFrom("x", "a")];

        var scores = BordaScorer.Score(movies, ballots);

        scores.Should().HaveCount(2);
        scores[1].MovieId.Should().Be("b");
        scores[1].Points.Should().Be(0);
    }

    [Fact]
    public void Score_UnknownOrRepeatedIdsOnABallot_AreIgnored()
    {
        string[] movies = ["a", "b"];
        Ballot[] ballots = [BallotFrom("x", "a", "zzz", "a", "b")];

        var scores = BordaScorer.Score(movies, ballots);

        scores.Select(score => score.Points).Should().Equal(2, 1);
    }

    [Fact]
    public void Score_NoBallots_ReturnsEveryMovieAtZero()
    {
        string[] movies = ["b", "a"];

        var scores = BordaScorer.Score(movies, []);

        scores.Select(score => score.MovieId).Should().Equal("a", "b");
        scores.Should().OnlyContain(score => score.Points == 0);
    }
}
