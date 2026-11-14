using System.Globalization;

namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights. Creates a night in <see cref="NightPhase.Pending"/> and emails only the
/// organizer, asking them to confirm the address is theirs. The invitations go out when they do
/// (<see cref="ConfirmNightService"/>).
/// </summary>
/// <remarks>
/// This endpoint needs no sign-in and mails an address a stranger typed, from an SES account other
/// applications share. Confirming first means the only mail an unconfirmed request can cause is
/// one email to the organizer address, at most three times a day, and it names nobody else. The
/// limits and the do-not-email list are checked before even that.
/// </remarks>
[UsedImplicitly]
internal class CreateNightService : IApiGatewayHandler
{
    internal const string LimitMessage = "You've planned as many movie nights as one person can in a day. Try again tomorrow.";

    internal const string ApplicationLimitMessage = "Movie Night Optimizer has planned as many movie nights as it can today. Try again tomorrow.";

    internal const string OrganizerOptedOutMessage = "This address has asked not to get email from Movie Night Optimizer, so it can't be sent the links.";

    internal const string EveryoneOptedOutMessage = "Everyone you added has asked not to get email from Movie Night Optimizer.";

    private readonly ApiGatewayAdapter _adapter;

    private readonly NightProvider _nightProvider;

    private readonly EmailSender _emailSender;

    private readonly DoNotEmailProvider _doNotEmailProvider;

    private readonly string _siteUrl;

    public CreateNightService(
        ApiGatewayAdapter adapter,
        NightProvider nightProvider,
        EmailSender emailSender,
        DoNotEmailProvider doNotEmailProvider
    )
    {
        _doNotEmailProvider = doNotEmailProvider ?? throw new ArgumentNullException(nameof(doNotEmailProvider));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _nightProvider = nightProvider ?? throw new ArgumentNullException(nameof(nightProvider));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _siteUrl = EnvReader.GetStringValue("SITE_URL");
    }

    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<CreateNightRequest, CreateNightResponse>(request, ExecuteAsync);

    internal async Task<Result<CreateNightResponse>> ExecuteAsync(CreateNightRequest request)
    {
        var organizerEmail = request.OrganizerEmail.TrimNullSafe();

        // The do-not-email list before the limits, so a night that can't go ahead spends neither.
        if(await _doNotEmailProvider.IsListedAsync(organizerEmail).ConfigureAwait(false))
            return new Result<CreateNightResponse>(new InvalidOperationException(OrganizerOptedOutMessage), HttpStatusCode.Conflict);

        var guests = (request.ParticipantEmails ?? [])
            .Select(email => email.TrimNullSafe())
            .ToImmutableList();

        var listed = await Task
            .WhenAll(guests.Select(email => _doNotEmailProvider.IsListedAsync(email)))
            .ConfigureAwait(false);

        // Left out of the night rather than kept in it unemailed, where every round would wait on
        // somebody nobody can reach.
        var reachableGuests = guests
            .Where((_, index) => !listed[index])
            .ToImmutableList();

        if(reachableGuests.Count == 0)
            return new Result<CreateNightResponse>(new InvalidOperationException(EveryoneOptedOutMessage), HttpStatusCode.Conflict);

        if(!await _nightProvider.TryReserveOrganizerSlotAsync(organizerEmail.ToMailboxKey()).ConfigureAwait(false))
            return new Result<CreateNightResponse>(new InvalidOperationException(LimitMessage), HttpStatusCode.TooManyRequests);

        if(!await _nightProvider.TryReserveApplicationSlotAsync().ConfigureAwait(false))
            return new Result<CreateNightResponse>(new InvalidOperationException(ApplicationLimitMessage), HttpStatusCode.TooManyRequests);

        // Every participant gets a token so that the item has a hash to hold, but only the
        // organizer's is ever sent. The guests' tokens are thrown away; confirming makes them
        // new ones and puts those in their invitations.
        var invitees = reachableGuests
            .Prepend(organizerEmail)
            .Select((email, index) => new Invitee
            {
                Email = email,
                IsOrganizer = index == 0,
                ParticipantId = Guid.NewGuid().ToString("N"),
                Token = SecretToken.Create()
            })
            .ToImmutableList();

        var night = new Night
        {
            NightId = Guid.NewGuid().ToString("N"),
            Date = DateOnly.ParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Phase = NightPhase.Pending,
            Participants = invitees
                .Select(invitee => new Participant
                {
                    ParticipantId = invitee.ParticipantId,
                    Email = invitee.Email,
                    IsOrganizer = invitee.IsOrganizer,
                    TokenHashes = [SecretToken.Hash(invitee.Token)]
                })
                .ToImmutableList()
        };

        await _nightProvider.SaveNewNightAsync(night).ConfigureAwait(false);

        var organizer = invitees[0];

        var failed = await _emailSender
            .SendEachAsync([
                new OutgoingEmail
                {
                    To = organizer.Email,
                    Content = NightEmails.Confirm(
                        night,
                        NightEmails.BuildLink(_siteUrl, night.NightId, organizer.Token),
                        NightEmails.BuildStopLink(_siteUrl, night.NightId, organizer.Token)
                    )
                }
            ])
            .ConfigureAwait(false);

        // Without that one email nobody can confirm, and the night can never start.
        if(failed > 0)
            return new Result<CreateNightResponse>(
                new InvalidOperationException("The confirmation email couldn't be sent. Try again in a few minutes."),
                HttpStatusCode.BadGateway
            );

        return new Result<CreateNightResponse>(
            new CreateNightResponse
            {
                NightId = night.NightId,
                InvitedCount = invitees.Count,
                OptedOutCount = guests.Count - reachableGuests.Count
            },
            HttpStatusCode.Created
        );
    }

    /// <summary>A participant before they are stored, while their token still exists to be sent.</summary>
    private record Invitee
    {
        public required string Email { get; init; }

        public required bool IsOrganizer { get; init; }

        public required string ParticipantId { get; init; }

        public required string Token { get; init; }
    }
}
