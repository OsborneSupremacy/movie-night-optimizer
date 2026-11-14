using System.Globalization;

namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights. Creates a night in <see cref="NightPhase.Collecting"/> and emails every
/// participant, organizer included, a link to the page where they suggest a movie.
/// </summary>
/// <remarks>
/// The daily limit comes before anything else. This endpoint sends mail to addresses a stranger
/// typed, from an SES account other applications share, and needs no sign-in to call.
/// </remarks>
[UsedImplicitly]
internal class CreateNightService : IApiGatewayHandler
{
    internal const string LimitMessage = "You've planned as many movie nights as one person can in a day. Try again tomorrow.";

    private readonly ApiGatewayAdapter _adapter;

    private readonly NightProvider _nightProvider;

    private readonly EmailSender _emailSender;

    private readonly string _siteUrl;

    public CreateNightService(ApiGatewayAdapter adapter, NightProvider nightProvider, EmailSender emailSender)
    {
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

        if(!await _nightProvider.TryReserveOrganizerSlotAsync(organizerEmail.ToMailboxKey()).ConfigureAwait(false))
            return new Result<CreateNightResponse>(new InvalidOperationException(LimitMessage), HttpStatusCode.TooManyRequests);

        var invitees = (request.ParticipantEmails ?? [])
            .Select(email => email.TrimNullSafe())
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
            Phase = NightPhase.Collecting,
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

        var failed = await _emailSender
            .SendEachAsync(invitees.Select(invitee => new OutgoingEmail
            {
                To = invitee.Email,
                Content = NightEmails.Suggest(night, organizerEmail, NightEmails.BuildLink(_siteUrl, night.NightId, invitee.Token))
            }))
            .ConfigureAwait(false);

        // Some mail going astray is survivable: the organizer can close the round without them.
        // None of it going is a night nobody can open, and the organizer should hear so now.
        if(failed == invitees.Count)
            return new Result<CreateNightResponse>(
                new InvalidOperationException("The invitations couldn't be sent. Try again in a few minutes."),
                HttpStatusCode.BadGateway
            );

        return new Result<CreateNightResponse>(new CreateNightResponse { NightId = night.NightId }, HttpStatusCode.Created);
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
