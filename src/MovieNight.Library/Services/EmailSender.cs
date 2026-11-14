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

    private readonly string _senderAddress;

    private readonly bool _liveMode;

    private readonly string _testRecipient;

    // ReSharper disable once ConvertToPrimaryConstructor
    public EmailSender(IAmazonSimpleEmailService sesClient)
    {
        _sesClient = sesClient ?? throw new ArgumentNullException(nameof(sesClient));
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
}
