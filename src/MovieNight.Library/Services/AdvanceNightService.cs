namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights/{nightId}/advance. Organizer only. Closes the current round without waiting
/// for whoever hasn't answered, so one person can't stall the night.
/// </summary>
[UsedImplicitly]
internal class AdvanceNightService : IApiGatewayHandler
{
    internal const string OrganizerOnlyMessage = "Only the person who planned this movie night can close a round.";

    internal const string AlreadyDoneMessage = "This movie night has already been decided.";

    internal const string NoSuggestionsMessage = "Nobody has suggested a movie yet.";

    internal const string NotConfirmedMessage = "Confirm this movie night first, so the invitations go out.";

    private readonly ApiGatewayAdapter _adapter;

    private readonly NightReader _nightReader;

    private readonly PhaseAdvancer _phaseAdvancer;

    public AdvanceNightService(ApiGatewayAdapter adapter, NightReader nightReader, PhaseAdvancer phaseAdvancer)
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

        if(night.Phase == NightPhase.Pending)
            return new Result<NightView>(new InvalidOperationException(NotConfirmedMessage), HttpStatusCode.Conflict);

        if(night.Phase == NightPhase.Done)
            return new Result<NightView>(new InvalidOperationException(AlreadyDoneMessage), HttpStatusCode.Conflict);

        // Closing collecting with nothing in it would decide the night on no movie at all.
        if(night.Phase == NightPhase.Collecting && NightProgress.Suggesters(night).Count == 0)
            return new Result<NightView>(new InvalidOperationException(NoSuggestionsMessage), HttpStatusCode.Conflict);

        // False means the last answer came in at the same moment and moved the night itself,
        // which is what the organizer wanted anyway.
        await _phaseAdvancer.TryAdvanceAsync(night).ConfigureAwait(false);

        return await _nightReader.ViewAsync(request.NightId, request.Token).ConfigureAwait(false);
    }
}
