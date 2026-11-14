namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights/{nightId}/suggestion. Records one movie for this participant while the night
/// is collecting, replacing any they suggested before. When it is the last one in, moves the
/// night on and emails everyone.
/// </summary>
[UsedImplicitly]
internal class SubmitSuggestionService : IApiGatewayHandler
{
    internal const string ClosedMessage = "Suggestions for this movie night are closed.";

    internal const string TakenMessage = "Great minds! Someone already suggested that one. Pick another?";

    private readonly ApiGatewayAdapter _adapter;

    private readonly NightReader _nightReader;

    private readonly NightProvider _nightProvider;

    private readonly PhaseAdvancer _phaseAdvancer;

    private readonly IMovieLookup _movieLookup;

    public SubmitSuggestionService(
        ApiGatewayAdapter adapter,
        NightReader nightReader,
        NightProvider nightProvider,
        PhaseAdvancer phaseAdvancer,
        IMovieLookup movieLookup
    )
    {
        _movieLookup = movieLookup ?? throw new ArgumentNullException(nameof(movieLookup));
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
        var body = _adapter.ReadBody<SuggestionBody>(request);

        if(body.IsFaulted)
            return ProxyResponseBuilder.Build(body.StatusCode, _adapter.Serialize(new ErrorResponse { Message = body.Exception.Message }));

        return await _adapter.AdaptAsync<SubmitSuggestionRequest, NightView>(
            new SubmitSuggestionRequest
            {
                NightId = request.GetPathParameter("nightId"),
                Token = request.GetParticipantToken(),
                Suggestion = body.Value.Suggestion.TrimNullSafe()
            },
            ExecuteAsync
        );
    }

    internal async Task<Result<NightView>> ExecuteAsync(SubmitSuggestionRequest request)
    {
        var caller = await _nightReader.FindCallerAsync(request.NightId, request.Token).ConfigureAwait(false);

        if(caller.IsFaulted)
            return new Result<NightView>(caller.Exception, caller.StatusCode);

        var (night, participant) = (caller.Value.Night, caller.Value.Participant);

        if(night.Phase != NightPhase.Collecting)
            return new Result<NightView>(new InvalidOperationException(ClosedMessage), HttpStatusCode.Conflict);

        // TMDB isn't asked about a title somebody already typed: the answer is no either way.
        if(NightProgress.IsTakenByAnother(night, participant.ParticipantId, request.Suggestion))
            return new Result<NightView>(new InvalidOperationException(TakenMessage), HttpStatusCode.Conflict);

        // Never fails: with no match the suggestion is saved as typed, as if TMDB didn't exist.
        var match = await _movieLookup.FindAsync(request.Suggestion).ConfigureAwait(false);
        var title = match.IsFound ? match.Title : request.Suggestion;

        if(match.IsFound && NightProgress.IsTakenByAnother(night, participant.ParticipantId, title, match.TmdbId))
            return new Result<NightView>(new InvalidOperationException(TakenMessage), HttpStatusCode.Conflict);

        var saved = await _nightProvider
            .SaveSuggestionAsync(night.NightId, participant.ParticipantId, title, match)
            .ConfigureAwait(false);

        if(!saved)
            return new Result<NightView>(new InvalidOperationException(ClosedMessage), HttpStatusCode.Conflict);

        var (found, updated) = await _nightProvider.TryGetNightAsync(night.NightId).ConfigureAwait(false);

        if(found && updated.Phase == NightPhase.Collecting && NightProgress.IsRoundComplete(updated))
            await _phaseAdvancer.TryAdvanceAsync(updated).ConfigureAwait(false);

        return await _nightReader.ViewAsync(request.NightId, request.Token).ConfigureAwait(false);
    }
}
