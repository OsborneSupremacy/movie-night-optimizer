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

    private readonly string _senderAddress;

    private readonly bool _liveMode;

    private readonly string _testRecipient;

    // ReSharper disable once ConvertToPrimaryConstructor
    public EmailSender(IAmazonSimpleEmailService sesClient, ILogger<EmailSender> logger)
    {
        _sesClient = sesClient ?? throw new ArgumentNullException(nameof(sesClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _senderAddress = EnvReader.GetStringValue("SENDER_ADDRESS");
        _liveMode = EnvReader.TryGetBooleanValue("LIVE_MODE", out var liveMode) && liveMode;
        _testRecipient = EnvReader.TryGetStringValue("TEST_RECIPIENT", out var testRecipient)
            ? testRecipient ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Sends one message to one person. Outside live mode it goes to the test recipient instead,
    /// with " - TEST MODE" on the subject, and is not sent at all if there is no test recipient.
    /// </summary>
    public async Task SendAsync(string to, string subject, string htmlBody, string textBody)
    {
        if(!_liveMode && string.IsNullOrWhiteSpace(_testRecipient))
            return;

        var request = new SendEmailRequest
        {
            Source = $"{ProductName} <{_senderAddress}>",
            Destination = new Destination { ToAddresses = [_liveMode ? to : _testRecipient] },
            Message = new Message
            {
                Subject = new Content(subject + (_liveMode ? string.Empty : " - TEST MODE")),
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
