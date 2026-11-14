namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights/{nightId}/opt-out. The "don't email me again" link in every email. Adds the
/// caller's inbox to the do-not-email list for good, and takes them out of this night's rounds.
/// </summary>
/// <remarks>
/// The token in the link is the proof: it only ever arrives in mail to that inbox. A POST, like
/// every other answer, so a mail scanner following the link can't unsubscribe anybody.
///
/// Taking them out of the rounds can complete one, if they were the last person it waited on, so
/// this moves the night on the same way a last answer would.
/// </remarks>
[UsedImplicitly]
internal class OptOutService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    private readonly NightReader _nightReader;

    private readonly NightProvider _nightProvider;

    private readonly DoNotEmailProvider _doNotEmailProvider;

    private readonly PhaseAdvancer _phaseAdvancer;

    public OptOutService(
        ApiGatewayAdapter adapter,
        NightReader nightReader,
        NightProvider nightProvider,
        DoNotEmailProvider doNotEmailProvider,
        PhaseAdvancer phaseAdvancer
    )
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _nightReader = nightReader ?? throw new ArgumentNullException(nameof(nightReader));
        _nightProvider = nightProvider ?? throw new ArgumentNullException(nameof(nightProvider));
        _doNotEmailProvider = doNotEmailProvider ?? throw new ArgumentNullException(nameof(doNotEmailProvider));
        _phaseAdvancer = phaseAdvancer ?? throw new ArgumentNullException(nameof(phaseAdvancer));
    }

    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<NightRequest, StatusCodeOnlyResponse>(
            new NightRequest
            {
                NightId = request.GetPathParameter("nightId"),
                Token = request.GetParticipantToken()
            },
            ExecuteAsync
        );

    internal async Task<Result<StatusCodeOnlyResponse>> ExecuteAsync(NightRequest request)
    {
        var caller = await _nightReader.FindCallerAsync(request.NightId, request.Token).ConfigureAwait(false);

        if(caller.IsFaulted)
            return new Result<StatusCodeOnlyResponse>(caller.Exception, caller.StatusCode);

        var (night, participant) = (caller.Value.Night, caller.Value.Participant);

        // The list first: it is what the person asked for, and it holds even if the rest fails.
        await _doNotEmailProvider.AddAsync(participant.Email).ConfigureAwait(false);
        await _nightProvider.MarkOptedOutAsync(night.NightId, participant.ParticipantId).ConfigureAwait(false);

        var (found, updated) = await _nightProvider.TryGetNightAsync(night.NightId).ConfigureAwait(false);

        // A round with nobody left in it who can answer would wait forever.
        if(found && updated.Phase != NightPhase.Done && NightProgress.IsRoundComplete(updated)
            && NightProgress.Suggesters(updated).Count > 0)
            await _phaseAdvancer.TryAdvanceAsync(updated).ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse(), HttpStatusCode.NoContent);
    }
}
