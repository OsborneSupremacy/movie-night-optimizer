using static MovieNight.Library.Tests.Fixtures.TestNights;

namespace MovieNight.Library.Tests.ServiceTests;

public class NightViewBuilderTests
{
    [Fact]
    public void Build_Ranking_ListsEveryoneElsesSuggestionButNeverTheirOwn()
    {
        var night = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big"), Person("c", "Cars"));

        var view = NightViewBuilder.Build(night, night.Participants[0]);

        view.ToRank.Select(movie => movie.Title).Should().BeEquivalentTo(["Big", "Cars"]);
        view.ToRank.Should().NotContain(movie => movie.MovieId == "a");
    }

    [Fact]
    public void Build_Collecting_ShowsNobodyElsesSuggestion()
    {
        var night = Night(NightPhase.Collecting, Person("a", "Alien"), Person("b", "Big"), Person("c"));

        var view = NightViewBuilder.Build(night, night.Participants[2]);

        view.ToRank.Should().BeEmpty();
        view.Results.Should().BeEmpty();
        view.YourSuggestion.Should().BeEmpty();
        view.AnsweredCount.Should().Be(2);
        view.ParticipantCount.Should().Be(3);
    }

    [Fact]
    public void Build_Done_ListsResultsWinnerFirstAndMarksTheirOwn()
    {
        var night = Night(NightPhase.Done,
            Person("a", "Alien", false, "b"),
            Person("b", "Big", false, "a"),
            Person("c", "", false, "b", "a"));

        var view = NightViewBuilder.Build(night, night.Participants[1]);

        view.Phase.Should().Be("done");
        view.Results.Select(result => result.Title).Should().Equal("Big", "Alien");
        view.Results[0].IsYours.Should().BeTrue();
        view.Results[1].IsYours.Should().BeFalse();
    }

    [Fact]
    public void Build_Organizer_SeesWhoTheyAreWaitingOn()
    {
        var night = Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b"));

        NightViewBuilder.Build(night, night.Participants[0]).WaitingOn.Should().Equal("b@example.com");
    }

    [Fact]
    public void Build_NotTheOrganizer_SeesNobodysAddress()
    {
        var night = Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b"));

        NightViewBuilder.Build(night, night.Participants[1]).WaitingOn.Should().BeEmpty();
    }
}
