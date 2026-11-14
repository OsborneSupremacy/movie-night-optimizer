namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights/{nightId}/ranking. Records this participant's ranking of the other
/// suggestions. When it is the last one in, moves the night to done and emails the results.
/// </summary>
[UsedImplicitly]
internal class SubmitRankingService : IApiGatewayHandler
{
    internal const string NotRankingMessage = "This movie night isn't taking rankings right now.";

    internal const string IncompleteMessage = "Rank every movie on the list, each one once.";

    private readonly ApiGatewayAdapter _adapter;

    private readonly NightReader _nightReader;

    private readonly NightProvider _nightProvider;

    private readonly PhaseAdvancer _phaseAdvancer;

    public SubmitRankingService(
        ApiGatewayAdapter adapter,
        NightReader nightReader,
        NightProvider nightProvider,
        PhaseAdvancer phaseAdvancer
    )
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _nightReader = nightReader ?? throw new ArgumentNullException(nameof(nightReader));
        _nightProvider = nightProvider ?? throw new ArgumentNullException(nameof(nightProvider));
        _phaseAdvancer = phaseAdvancer ?? throw new ArgumentNullException(nameof(phaseAdvancer));
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    )
    {
        var body = _adapter.ReadBody<RankingBody>(request);

        if(body.IsFaulted)
            return ProxyResponseBuilder.Build(body.StatusCode, _adapter.Serialize(new ErrorResponse { Message = body.Exception.Message }));

        return await _adapter.AdaptAsync<SubmitRankingRequest, NightView>(
            new SubmitRankingRequest
            {
                NightId = request.GetPathParameter("nightId"),
                Token = request.GetParticipantToken(),
                // A body can say null where the record says it can't.
                Ranking = (body.Value.Ranking ?? [])
                    .Select(movieId => movieId.TrimNullSafe())
                    .ToImmutableList()
            },
            ExecuteAsync
        );
    }

    internal async Task<Result<NightView>> ExecuteAsync(SubmitRankingRequest request)
    {
        var caller = await _nightReader.FindCallerAsync(request.NightId, request.Token).ConfigureAwait(false);

        if(caller.IsFaulted)
            return new Result<NightView>(caller.Exception, caller.StatusCode);

        var (night, participant) = (caller.Value.Night, caller.Value.Participant);

        if(night.Phase != NightPhase.Ranking)
            return new Result<NightView>(new InvalidOperationException(NotRankingMessage), HttpStatusCode.Conflict);

        if(!NightProgress.IsCompleteRanking(night, participant.ParticipantId, request.Ranking))
            return new Result<NightView>(new InvalidOperationException(IncompleteMessage), HttpStatusCode.BadRequest);

        var saved = await _nightProvider
            .SaveRankingAsync(night.NightId, participant.ParticipantId, request.Ranking)
            .ConfigureAwait(false);

        if(!saved)
            return new Result<NightView>(new InvalidOperationException(NotRankingMessage), HttpStatusCode.Conflict);

        var (found, updated) = await _nightProvider.TryGetNightAsync(night.NightId).ConfigureAwait(false);

        if(found && updated.Phase == NightPhase.Ranking && NightProgress.IsRoundComplete(updated))
            await _phaseAdvancer.TryAdvanceAsync(updated).ConfigureAwait(false);

        return await _nightReader.ViewAsync(request.NightId, request.Token).ConfigureAwait(false);
    }
}
