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
    public void Build_MoviesTmdbFound_CarryTheirYearAndAPosterUrl()
    {
        var night = Night(NightPhase.Ranking,
            Person("a", "Alien") with { TmdbId = 348, MovieYear = "1979", PosterPath = "/alien.jpg" },
            Person("b", "Home videos"));

        var view = NightViewBuilder.Build(night, night.Participants[1]);

        view.ToRank.Single().Year.Should().Be("1979");
        view.ToRank.Single().PosterUrl.Should().Be("https://image.tmdb.org/t/p/w185/alien.jpg");
        view.ToRank.Single().TmdbUrl.Should().Be("https://www.themoviedb.org/movie/348");
        NightViewBuilder.Build(night, night.Participants[0]).YourTmdbUrl.Should().Be("https://www.themoviedb.org/movie/348");
        view.YourTmdbUrl.Should().BeEmpty("TMDB didn't find the home videos");
        NightViewBuilder.Build(night, night.Participants[0]).ToRank.Single().PosterUrl.Should().BeEmpty();
    }

    [Fact]
    public void Build_Organizer_SeesWhoTheyAreWaitingOn()
    {
        var night = Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b"));

        NightViewBuilder.Build(night, night.Participants[0]).WaitingOn.Should().Equal("b@example.com");
    }

    [Fact]
    public void Build_PendingNight_ShowsTheOrganizerWhoWillBeInvited()
    {
        var night = Night(NightPhase.Pending, Person("a", isOrganizer: true), Person("b"), Person("c") with { OptedOut = true });

        var view = NightViewBuilder.Build(night, night.Participants[0]);

        view.Phase.Should().Be("pending");
        view.Guests.Should().Equal("b@example.com");
        view.WaitingOn.Should().BeEmpty();
        NightViewBuilder.Build(night, night.Participants[1]).Guests.Should().BeEmpty();
    }

    [Fact]
    public void Build_NotTheOrganizer_SeesNobodysAddress()
    {
        var night = Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b"));

        NightViewBuilder.Build(night, night.Participants[1]).WaitingOn.Should().BeEmpty();
    }
}
