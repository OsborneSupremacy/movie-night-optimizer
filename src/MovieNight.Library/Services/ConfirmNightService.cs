namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights/{nightId}/confirm. Organizer only. Proves the organizer owns the address they
/// typed, since the token only ever went to that inbox, and sends the guests their invitations.
/// </summary>
/// <remarks>
/// A POST, like every other answer, so a mail scanner following the link in the confirmation
/// email can't confirm for anybody. Pressing it again, or on a night already under way, changes
/// nothing and answers with the night as it is.
/// </remarks>
[UsedImplicitly]
internal class ConfirmNightService : IApiGatewayHandler
{
    internal const string OrganizerOnlyMessage = "Only the person who planned this movie night can confirm it.";

    private readonly ApiGatewayAdapter _adapter;

    private readonly NightReader _nightReader;

    private readonly PhaseAdvancer _phaseAdvancer;

    public ConfirmNightService(ApiGatewayAdapter adapter, NightReader nightReader, PhaseAdvancer phaseAdvancer)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _nightReader = nightReader ?? throw new ArgumentNullException(nameof(nightReader));
        _phaseAdvancer = phaseAdvancer ?? throw new ArgumentNullException(nameof(phaseAdvancer));
    }

    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<NightRequest, NightView>(
            new NightRequest
            {
                NightId = request.GetPathParameter("nightId"),
                Token = request.GetParticipantToken()
            },
            ExecuteAsync
        );

    internal async Task<Result<NightView>> ExecuteAsync(NightRequest request)
    {
        var caller = await _nightReader.FindCallerAsync(request.NightId, request.Token).ConfigureAwait(false);

        if(caller.IsFaulted)
            return new Result<NightView>(caller.Exception, caller.StatusCode);

        var (night, participant) = (caller.Value.Night, caller.Value.Participant);

        if(!participant.IsOrganizer)
            return new Result<NightView>(new InvalidOperationException(OrganizerOnlyMessage), HttpStatusCode.Forbidden);

        // The phase change is conditional on Pending, so a double click or two tabs can only send
        // the invitations once.
        if(night.Phase == NightPhase.Pending)
            await _phaseAdvancer.TryAdvanceAsync(night).ConfigureAwait(false);

        return await _nightReader.ViewAsync(request.NightId, request.Token).ConfigureAwait(false);
    }
}
