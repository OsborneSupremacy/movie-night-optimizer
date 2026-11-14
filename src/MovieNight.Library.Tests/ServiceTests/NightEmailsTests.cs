using static MovieNight.Library.Tests.Fixtures.TestNights;

namespace MovieNight.Library.Tests.ServiceTests;

public class NightEmailsTests
{
    private const string Link = "https://movienight.example.com/night/night1#t=abc";

    private const string StopLink = "https://movienight.example.com/night/night1/stop#t=abc";

    [Fact]
    public void BuildLink_PutsTheTokenInTheFragment() =>
        NightEmails.BuildLink("https://movienight.example.com/", "night1", "abc")
            .Should().Be("https://movienight.example.com/night/night1#t=abc");

    [Fact]
    public void Suggest_HasTheLinkInBothParts()
    {
        var email = NightEmails.Suggest(Night(NightPhase.Collecting, Person("a")), "a@example.com", Link, StopLink);

        email.HtmlBody.Should().Contain(Link);
        email.TextBody.Should().Contain(Link);
        email.Subject.Should().Be("Pick a movie for Saturday, November 14");
    }

    [Fact]
    public void Suggest_EncodesTheOrganizersAddressInTheHtml()
    {
        var email = NightEmails.Suggest(Night(NightPhase.Collecting, Person("a")), "<b>x</b>@example.com", Link, StopLink);

        email.HtmlBody.Should().NotContain("<b>x</b>");
        email.HtmlBody.Should().Contain("&lt;b&gt;x&lt;/b&gt;");
    }

    [Fact]
    public void Results_EncodesMovieTitlesInTheHtml()
    {
        // A title is typed by one participant and lands in everyone else's inbox.
        var night = Night(NightPhase.Done,
            Person("a", "<script>alert(1)</script>", false, "b"),
            Person("b", "Big", false, "a"));

        var email = NightEmails.Results(night, Link, StopLink);

        email.HtmlBody.Should().NotContain("<script>");
        email.TextBody.Should().Contain("<script>alert(1)</script>");
    }

    [Fact]
    public void Results_NamesTheWinnerInTheSubject()
    {
        var night = Night(NightPhase.Done,
            Person("a", "Alien", false, "b"),
            Person("b", "Big", false, "a"),
            Person("c", "", false, "b", "a"));

        NightEmails.Results(night, Link, StopLink).Subject.Should().Be("Movie night Saturday, November 14: Big");
    }

    [Fact]
    public void Results_SaysHowLongTheWinnerIsAndWhereToWatchIt()
    {
        var details = MovieDetails.Empty with { Certification = "PG", RuntimeMinutes = 104, StreamingOn = ["HBO Max", "Netflix"] };
        var night = Night(NightPhase.Done,
            Person("a", "Paddington 2", false, "b") with { TmdbId = 346648, MovieYear = "2017", Details = details },
            Person("b", "Big", false, "a"),
            Person("c", "", false, "a", "b"));

        var email = NightEmails.Results(night, Link, StopLink);

        email.TextBody.Should().Contain("Rated PG · 1h 44m").And.Contain("Streaming on HBO Max, Netflix.").And.Contain("JustWatch");
        email.TextBody.Should().Contain("More about it on TMDB: https://www.themoviedb.org/movie/346648");
        email.HtmlBody.Should().Contain("href=\"https://www.themoviedb.org/movie/346648\"");
        email.HtmlBody.Should().Contain("Streaming on HBO Max, Netflix.").And.Contain("JustWatch");
    }

    [Fact]
    public void Results_WinnerWithNoDetails_SaysNothingAboutThem()
    {
        var night = Night(NightPhase.Done, Person("a", "Alien", false, "b"), Person("b", "Big", false, "a"), Person("c", "", false, "a", "b"));

        var email = NightEmails.Results(night, Link, StopLink);

        email.TextBody.Should().NotContain("Streaming on").And.NotContain("Rated").And.NotContain("JustWatch").And.NotContain("TMDB");
    }

    [Fact]
    public void Results_NoSuggestions_SaysSoRatherThanNamingAWinner()
    {
        var night = Night(NightPhase.Done, Person("a"), Person("b"));

        NightEmails.Results(night, Link, StopLink).Subject.Should().Be("No movie for Saturday, November 14");
    }

    [Fact]
    public void TestModeSubject_PutsWhoItWasForFirst() =>
        // Every diverted message lands in one inbox, each with a different person's link.
        EmailSender.TestModeSubject("one@example.com", "Pick a movie for Saturday, November 14")
            .Should().Be("[TEST for one@example.com] Pick a movie for Saturday, November 14");

    [Fact]
    public void BuildStopLink_PutsTheTokenInTheFragment() =>
        NightEmails.BuildStopLink("https://movienight.example.com", "night1", "abc").Should().Be(StopLink);

    [Fact]
    public void EveryEmail_HasTheStopLinkInBothParts()
    {
        var collecting = Night(NightPhase.Collecting, Person("a", "Alien"), Person("b", "Big"));
        var done = Night(NightPhase.Done, Person("a", "Alien", false, "b"), Person("b", "Big", false, "a"));

        EmailContent[] emails =
        [
            NightEmails.Suggest(collecting, "a@example.com", Link, StopLink),
            NightEmails.Rank(collecting, Link, StopLink),
            NightEmails.Results(done, Link, StopLink),
            NightEmails.Results(Night(NightPhase.Done, Person("a")), Link, StopLink)
        ];

        emails.Should().OnlyContain(email => email.HtmlBody.Contains(StopLink) && email.TextBody.Contains(StopLink));
    }

    [Fact]
    public void Rank_CountsTheSuggestions()
    {
        var night = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big"), Person("c", "Cars"));

        NightEmails.Rank(night, Link, StopLink).TextBody.Should().Contain("3 movies have been suggested");
    }
}
