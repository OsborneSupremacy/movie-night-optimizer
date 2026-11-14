namespace MovieNight.Library.Services;

/// <summary>
/// Closes the round a night is in and sends everyone the email for the next one.
/// </summary>
/// <remarks>
/// Called both when the last person answers and when the organizer closes a round early. Either
/// can happen at the same moment as the other, or twice at once, so the phase change is
/// conditional on the phase the night was read in, and only the caller whose write lands sends
/// any mail.
/// </remarks>
[UsedImplicitly]
internal class PhaseAdvancer
{
    private readonly NightProvider _nightProvider;

    private readonly EmailSender _emailSender;

    private readonly ILogger<PhaseAdvancer> _logger;

    private readonly string _siteUrl;

    // ReSharper disable once ConvertToPrimaryConstructor
    public PhaseAdvancer(NightProvider nightProvider, EmailSender emailSender, ILogger<PhaseAdvancer> logger)
    {
        _nightProvider = nightProvider ?? throw new ArgumentNullException(nameof(nightProvider));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _siteUrl = EnvReader.GetStringValue("SITE_URL");
    }

    /// <summary>
    /// Moves <paramref name="night"/> on from the phase it was read in. False if somebody else
    /// already had, in which case they sent the email and this sends nothing.
    /// </summary>
    public async Task<bool> TryAdvanceAsync(Night night)
    {
        if(night.Phase == NightPhase.Done)
            return false;

        var next = NightProgress.NextPhase(night);

        // Only a token's hash is kept, so the old links can't be sent again. Each round's email
        // carries a new token, added alongside the old ones so that every link still works.
        var tokens = night.Participants
            .ToImmutableDictionary(participant => participant.ParticipantId, _ => SecretToken.Create());

        var advanced = await _nightProvider
            .AdvancePhaseAsync(
                night.NightId,
                night.Phase,
                next,
                tokens.ToImmutableDictionary(entry => entry.Key, entry => SecretToken.Hash(entry.Value))
            )
            .ConfigureAwait(false);

        if(!advanced)
            return false;

        // Read again rather than reuse what was passed in: a suggestion or ranking can have landed
        // between that read and the phase change, and the email should count it.
        var (found, advancedNight) = await _nightProvider.TryGetNightAsync(night.NightId).ConfigureAwait(false);

        if(!found)
            return true;

        var emails = advancedNight.Participants
            .Select(participant =>
            {
                var token = tokens[participant.ParticipantId];
                var link = NightEmails.BuildLink(_siteUrl, advancedNight.NightId, token);
                var stopLink = NightEmails.BuildStopLink(_siteUrl, advancedNight.NightId, token);

                return new OutgoingEmail
                {
                    To = participant.Email,
                    Content = next == NightPhase.Ranking
                        ? NightEmails.Rank(advancedNight, link, stopLink)
                        : NightEmails.Results(advancedNight, link, stopLink)
                };
            })
            .ToImmutableList();

        var failed = await _emailSender.SendEachAsync(emails).ConfigureAwait(false);

        if(failed > 0)
            _logger.LogError("Night {NightId} moved to {Phase}, but {Failed} of {Total} emails failed",
                advancedNight.NightId, next, failed, emails.Count);

        return true;
    }
}
