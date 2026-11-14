using static MovieNight.Library.Tests.Fixtures.TestNights;

namespace MovieNight.Library.Tests.ServiceTests;

public class NightEmailsTests
{
    private const string Link = "https://movienight.example.com/night/night1#t=abc";

    [Fact]
    public void BuildLink_PutsTheTokenInTheFragment() =>
        NightEmails.BuildLink("https://movienight.example.com/", "night1", "abc")
            .Should().Be("https://movienight.example.com/night/night1#t=abc");

    [Fact]
    public void Suggest_HasTheLinkInBothParts()
    {
        var email = NightEmails.Suggest(Night(NightPhase.Collecting, Person("a")), "a@example.com", Link);

        email.HtmlBody.Should().Contain(Link);
        email.TextBody.Should().Contain(Link);
        email.Subject.Should().Be("Pick a movie for Saturday, November 14");
    }

    [Fact]
    public void Suggest_EncodesTheOrganizersAddressInTheHtml()
    {
        var email = NightEmails.Suggest(Night(NightPhase.Collecting, Person("a")), "<b>x</b>@example.com", Link);

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

        var email = NightEmails.Results(night, Link);

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

        NightEmails.Results(night, Link).Subject.Should().Be("Movie night Saturday, November 14: Big");
    }

    [Fact]
    public void Results_NoSuggestions_SaysSoRatherThanNamingAWinner()
    {
        var night = Night(NightPhase.Done, Person("a"), Person("b"));

        NightEmails.Results(night, Link).Subject.Should().Be("No movie for Saturday, November 14");
    }

    [Fact]
    public void TestModeSubject_PutsWhoItWasForFirst() =>
        // Every diverted message lands in one inbox, each with a different person's link.
        EmailSender.TestModeSubject("one@example.com", "Pick a movie for Saturday, November 14")
            .Should().Be("[TEST for one@example.com] Pick a movie for Saturday, November 14");

    [Fact]
    public void Rank_CountsTheSuggestions()
    {
        var night = Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big"), Person("c", "Cars"));

        NightEmails.Rank(night, Link).TextBody.Should().Contain("3 movies have been suggested");
    }
}
