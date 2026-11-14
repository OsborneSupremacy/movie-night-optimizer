using static MovieNight.Library.Tests.Fixtures.TestNights;

namespace MovieNight.Library.Tests.ServiceTests;

public class NightProgressTests
{
    [Fact]
    public void MoviesToRank_NeverIncludesTheParticipantsOwnSuggestion()
    {
        var night = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big"), Person("c", "Cars"));

        NightProgress.MoviesToRank(night, "a").Select(movie => movie.ParticipantId)
            .Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public void MoviesToRank_LeavesOutPeopleWhoSuggestedNothing()
    {
        var night = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b"), Person("c", "Cars"));

        NightProgress.MoviesToRank(night, "b").Select(movie => movie.ParticipantId)
            .Should().BeEquivalentTo(["a", "c"]);
    }

    [Fact]
    public void MoviesToRank_SameNightTwice_GivesTheSameOrder()
    {
        var night = Night(NightPhase.Ranking,
            Person("a", "A"), Person("b", "B"), Person("c", "C"), Person("d", "D"), Person("e", "E"));

        NightProgress.MoviesToRank(night, "a").Should().Equal(NightProgress.MoviesToRank(night, "a"));
    }

    [Fact]
    public void HasAnswered_Collecting_IsWhetherTheyHaveSuggested()
    {
        var night = Night(NightPhase.Collecting, Person("a", "Alien"), Person("b"));

        NightProgress.HasAnswered(night, night.Participants[0]).Should().BeTrue();
        NightProgress.HasAnswered(night, night.Participants[1]).Should().BeFalse();
    }

    [Fact]
    public void HasAnswered_RankingWithNothingToRank_CountsAsAnswered()
    {
        // "c" suggested nothing and has two movies to rank. Where somebody's own movie is the only
        // one, they have nothing to rank, and must not hold the round open.
        var night = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big"), Person("c"));
        var onlyMovieIsTheirs = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b"));

        NightProgress.HasAnswered(night, night.Participants[2]).Should().BeFalse();
        NightProgress.HasAnswered(onlyMovieIsTheirs, onlyMovieIsTheirs.Participants[0]).Should().BeTrue();
    }

    [Fact]
    public void IsRoundComplete_EveryoneRanked_IsTrue()
    {
        var night = Night(NightPhase.Ranking,
            Person("a", "Alien", false, "b"),
            Person("b", "Big", false, "a"));

        NightProgress.IsRoundComplete(night).Should().BeTrue();
    }

    [Fact]
    public void IsRoundComplete_SomebodyStillToSuggest_IsFalse()
    {
        var night = Night(NightPhase.Collecting, Person("a", "Alien"), Person("b"));

        NightProgress.IsRoundComplete(night).Should().BeFalse();
    }

    [Fact]
    public void NextPhase_CollectingWithTwoOrMoreSuggestions_IsRanking() =>
        NightProgress.NextPhase(Night(NightPhase.Collecting, Person("a", "Alien"), Person("b", "Big")))
            .Should().Be(NightPhase.Ranking);

    [Fact]
    public void NextPhase_CollectingWithOneSuggestion_SkipsStraightToDone() =>
        NightProgress.NextPhase(Night(NightPhase.Collecting, Person("a", "Alien"), Person("b")))
            .Should().Be(NightPhase.Done);

    [Fact]
    public void NextPhase_Ranking_IsDone() =>
        NightProgress.NextPhase(Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big")))
            .Should().Be(NightPhase.Done);

    [Theory]
    [InlineData(new[] { "b", "c" }, true)]
    [InlineData(new[] { "c", "b" }, true)]
    [InlineData(new[] { "b" }, false)]
    [InlineData(new[] { "b", "b" }, false)]
    [InlineData(new[] { "a", "b" }, false)]
    [InlineData(new[] { "b", "c", "zzz" }, false)]
    public void IsCompleteRanking_RequiresEveryOtherMovieExactlyOnce(string[] ranking, bool expected)
    {
        var night = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big"), Person("c", "Cars"));

        NightProgress.IsCompleteRanking(night, "a", ranking).Should().Be(expected);
    }

    [Fact]
    public void Score_UsesTheStoredRankings()
    {
        var night = Night(NightPhase.Done,
            Person("a", "Alien", false, "b", "c"),
            Person("b", "Big", false, "c", "a"),
            Person("c", "Cars", false, "b", "a"));

        NightProgress.Score(night).Select(score => score.MovieId).Should().Equal("b", "c", "a");
    }

    [Fact]
    public void FindByToken_MatchesAnyOfTheParticipantsTokens()
    {
        var second = Person("b") with { TokenHashes = [SecretToken.Hash("first"), SecretToken.Hash("second")] };
        var night = Night(NightPhase.Ranking, Person("a"), second);

        NightProgress.FindByToken(night, "first").participant.ParticipantId.Should().Be("b");
        NightProgress.FindByToken(night, "second").participant.ParticipantId.Should().Be("b");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    public void FindByToken_UnknownOrBlank_IsNotFound(string token)
    {
        var night = Night(NightPhase.Collecting, Person("a"));

        NightProgress.FindByToken(night, token).found.Should().BeFalse();
    }
}
