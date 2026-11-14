using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

namespace MovieNight.Library.Services;

/// <summary>
/// The one place mail leaves from, so that who it is from and whether it really goes to the
/// person it is addressed to are decided once.
/// </summary>
[UsedImplicitly]
internal class EmailSender
{
    private const string ProductName = "Movie Night Optimizer";

    private readonly IAmazonSimpleEmailService _sesClient;

    private readonly ILogger<EmailSender> _logger;

    private readonly DoNotEmailProvider _doNotEmailProvider;

    private readonly string _senderAddress;

    private readonly bool _liveMode;

    private readonly string _testRecipient;

    // ReSharper disable once ConvertToPrimaryConstructor
    public EmailSender(IAmazonSimpleEmailService sesClient, DoNotEmailProvider doNotEmailProvider, ILogger<EmailSender> logger)
    {
        _sesClient = sesClient ?? throw new ArgumentNullException(nameof(sesClient));
        _doNotEmailProvider = doNotEmailProvider ?? throw new ArgumentNullException(nameof(doNotEmailProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _senderAddress = EnvReader.GetStringValue("SENDER_ADDRESS");
        _liveMode = EnvReader.TryGetBooleanValue("LIVE_MODE", out var liveMode) && liveMode;
        _testRecipient = EnvReader.TryGetStringValue("TEST_RECIPIENT", out var testRecipient)
            ? testRecipient ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Sends one message to one person. Outside live mode it goes to the test recipient instead,
    /// with <see cref="TestModeSubject"/>, and is not sent at all if there is no test recipient.
    /// Never sent to an address on the do-not-email list, in either mode.
    /// </summary>
    /// <remarks>
    /// The list is checked here, the one place mail leaves from, rather than by each caller, so no
    /// new email can forget to.
    /// </remarks>
    public async Task SendAsync(string to, string subject, string htmlBody, string textBody)
    {
        if(!_liveMode && string.IsNullOrWhiteSpace(_testRecipient))
            return;

        if(await _doNotEmailProvider.IsListedAsync(to).ConfigureAwait(false))
            return;

        var request = new SendEmailRequest
        {
            Source = $"{ProductName} <{_senderAddress}>",
            Destination = new Destination { ToAddresses = [_liveMode ? to : _testRecipient] },
            Message = new Message
            {
                Subject = new Content(_liveMode ? subject : TestModeSubject(to, subject)),
                Body = new Body
                {
                    Html = new Content(htmlBody),
                    Text = new Content(textBody)
                }
            }
        };

        await _sesClient.SendEmailAsync(request).ConfigureAwait(false);
    }

    /// <summary>
    /// The subject a diverted message carries: who it was really for, first.
    /// </summary>
    /// <remarks>
    /// In test mode every participant's mail lands in one inbox, and each carries a different
    /// person's link. Without the address up front there is no telling whose link is whose.
    /// </remarks>
    internal static string TestModeSubject(string to, string subject) =>
        $"[TEST for {to}] {subject}";

    /// <summary>
    /// Sends each message on its own, so one address SES refuses doesn't stop the rest. Returns
    /// how many failed; each failure is logged.
    /// </summary>
    /// <remarks>
    /// The caller has always written the night by now, so a failure here can't be undone by
    /// throwing. What it can do is tell the person who pressed the button.
    /// </remarks>
    public async Task<int> SendEachAsync(IEnumerable<OutgoingEmail> emails)
    {
        var results = await Task
            .WhenAll(emails.Select(async email =>
            {
                try
                {
                    await SendAsync(email.To, email.Content.Subject, email.Content.HtmlBody, email.Content.TextBody)
                        .ConfigureAwait(false);
                    return true;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Couldn't send {Subject}", email.Content.Subject);
                    return false;
                }
            }))
            .ConfigureAwait(false);

        return results.Count(sent => !sent);
    }
}

internal record OutgoingEmail
{
    public required string To { get; init; }

    public required EmailContent Content { get; init; }
}
