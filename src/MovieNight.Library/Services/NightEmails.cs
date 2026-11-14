using System.Globalization;

namespace MovieNight.Library.Services;

internal record EmailContent
{
    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public required string TextBody { get; init; }
}

/// <summary>
/// The three emails a night sends: suggest, rank, results.
/// </summary>
/// <remarks>
/// Movie titles and the organizer's address are typed by people and land in other people's
/// inboxes, so everything that didn't come from this file is HTML-encoded on its way into the
/// HTML part. The plain text part needs no encoding.
/// </remarks>
internal static class NightEmails
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The link a participant follows. The token is in the fragment, which browsers never send to a server.</summary>
    public static string BuildLink(string siteUrl, string nightId, string token) =>
        $"{siteUrl.TrimEnd('/')}/night/{Uri.EscapeDataString(nightId)}#t={token}";

    /// <summary>
    /// The "don't email me again" link. The same token as the email's main link: it only ever
    /// arrives in mail to this inbox, which is what makes it proof enough.
    /// </summary>
    public static string BuildStopLink(string siteUrl, string nightId, string token) =>
        $"{siteUrl.TrimEnd('/')}/night/{Uri.EscapeDataString(nightId)}/stop#t={token}";

    private const string StopText = "Don't want email from Movie Night Optimizer? Stop it for good";

    public static EmailContent Suggest(Night night, string organizerEmail, string link, string stopLink)
    {
        var date = FormatDate(night.Date);
        var invitation = $"{organizerEmail} added your address to a movie night on {date}, and wants your pick.";
        const string Ask = "Suggest one movie. When everyone has, you'll get an email to rank the others' picks.";

        return new EmailContent
        {
            Subject = $"Pick a movie for {date}",
            HtmlBody = Layout(
                "What should we watch?",
                [Encode(invitation), Encode(Ask)],
                "Suggest a movie",
                link,
                stopLink
            ),
            TextBody = $"{invitation}\n\n{Ask}\n\nSuggest a movie: {link}\n" + TextFooter(stopLink)
        };
    }

    public static EmailContent Rank(Night night, string link, string stopLink)
    {
        var date = FormatDate(night.Date);
        var count = NightProgress.Suggesters(night).Count;
        var summary = $"{count} movies have been suggested for {date}.";
        const string Ask = "Put the ones you didn't suggest in order, favorite first. The best-ranked movie wins.";

        return new EmailContent
        {
            Subject = $"Rank the movies for {date}",
            HtmlBody = Layout(
                "The suggestions are in",
                [Encode(summary), Encode(Ask)],
                "Rank the movies",
                link,
                stopLink
            ),
            TextBody = $"{summary}\n\n{Ask}\n\nRank the movies: {link}\n" + TextFooter(stopLink)
        };
    }

    public static EmailContent Results(Night night, string link, string stopLink)
    {
        var date = FormatDate(night.Date);
        var scores = NightProgress.Score(night);

        if(scores.Count == 0)
        {
            const string Nothing = "Nobody suggested a movie, so there's nothing to watch. Maybe a board game?";

            return new EmailContent
            {
                Subject = $"No movie for {date}",
                HtmlBody = Layout("No movie this time", [Encode(Nothing)], "See the night", link, stopLink),
                TextBody = $"{Nothing}\n\nSee the night: {link}\n" + TextFooter(stopLink)
            };
        }

        var winningMovie = NightProgress.SuggesterOf(night, scores[0].MovieId);
        var winner = NightProgress.DisplayTitle(winningMovie);
        var headline = $"You're watching {winner}.";

        var lines = scores
            .Select((score, index) =>
                $"{index + 1}. {NightProgress.DisplayTitle(NightProgress.SuggesterOf(night, score.MovieId))} ({Points(score.Points)})")
            .ToImmutableList();

        // Mail clients often hold back remote images until asked, so the poster is a nicety and the
        // email reads the same without it.
        var posterUrl = NightProgress.PosterUrl(winningMovie.PosterPath, "w342");
        var poster = posterUrl.Length > 0
            ? $"<p style=\"margin:0 0 16px;\"><img src=\"{Encode(posterUrl)}\" alt=\"Poster for {Encode(winner)}\""
                + " width=\"160\" style=\"display:block;width:160px;height:auto;border-radius:8px;\"></p>"
            : string.Empty;

        var list = poster
            + "<ol style=\"padding-left:1.25rem;margin:0 0 1rem;\">"
            + string.Concat(scores.Select(score =>
                $"<li style=\"margin:0 0 .25rem;\">{Encode(NightProgress.DisplayTitle(NightProgress.SuggesterOf(night, score.MovieId)))}"
                + $" <span style=\"color:#6b6478;\">({Points(score.Points)})</span></li>"))
            + "</ol>";

        return new EmailContent
        {
            Subject = $"Movie night {date}: {winner}",
            HtmlBody = Layout(
                headline,
                [Encode($"Everyone's rankings are in for {date}. Here's how the movies finished:")],
                "See the results",
                link,
                stopLink,
                list
            ),
            TextBody = $"{headline}\n\nEveryone's rankings are in for {date}. Here's how the movies finished:\n\n"
                + string.Join("\n", lines)
                + $"\n\nSee the results: {link}\n"
                + TextFooter(stopLink)
        };
    }

    private static string TextFooter(string stopLink) =>
        $"\nThis link is yours alone. Please don't forward it.\n{StopText}: {stopLink}\n";

    private static string FormatDate(DateOnly date) => date.ToString("dddd, MMMM d", English);

    private static string Points(int points) => points == 1 ? "1 point" : $"{points} points";

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    /// <param name="paragraphs">Already encoded.</param>
    /// <param name="extraHtml">Already encoded. Goes between the paragraphs and the button.</param>
    private static string Layout(
        string heading,
        IEnumerable<string> paragraphs,
        string buttonText,
        string link,
        string stopLink,
        string extraHtml = ""
    ) =>
        "<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f2f7;"
        + "font-family:-apple-system,'Segoe UI',Roboto,sans-serif;color:#1f1c28;\">"
        + "<div style=\"max-width:480px;margin:0 auto;background:#ffffff;border-radius:12px;padding:28px;\">"
        + "<p style=\"margin:0 0 16px;font-weight:700;color:#8a6d00;\">Movie Night Optimizer</p>"
        + $"<h1 style=\"font-size:22px;line-height:1.25;margin:0 0 16px;\">{Encode(heading)}</h1>"
        + string.Concat(paragraphs.Select(paragraph =>
            $"<p style=\"font-size:16px;line-height:1.5;margin:0 0 16px;\">{paragraph}</p>"))
        + extraHtml
        + $"<p style=\"margin:24px 0;\"><a href=\"{Encode(link)}\" style=\"display:inline-block;background:#f5c518;"
        + "color:#1a1500;font-weight:700;text-decoration:none;padding:12px 20px;border-radius:8px;\">"
        + $"{Encode(buttonText)}</a></p>"
        + "<p style=\"font-size:13px;line-height:1.5;color:#6b6478;margin:0 0 8px;\">This link is yours alone. "
        + "Please don't forward it.</p>"
        + "<p style=\"font-size:13px;line-height:1.5;color:#6b6478;margin:0;\">"
        + $"<a href=\"{Encode(stopLink)}\" style=\"color:#6b6478;\">{Encode(StopText)}</a>.</p>"
        + "</div></body></html>";
}
