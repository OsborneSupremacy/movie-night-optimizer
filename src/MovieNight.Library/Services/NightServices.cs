namespace MovieNight.Library.Services;

// The four handlers that are still stubs, kept together until each is built and moved to a file
// of its own. Every one identifies the caller the same way: hash the token from
// request.GetParticipantToken() and find the participant in the night whose TokenHash matches.
// No match is a 404, the same answer as a night that doesn't exist.

/// <summary>
/// GET /api/nights/{nightId}. What this participant should see: the phase, whether they have
/// answered yet, the suggestions they can rank (never their own), and the results once done.
/// </summary>
[UsedImplicitly]
internal class GetNightService : IApiGatewayHandler
{
    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        Task.FromResult(ProxyResponseBuilder.Build(HttpStatusCode.NotImplemented));
}

/// <summary>
/// POST /api/nights/{nightId}/suggestion. Records one movie for this participant while the night
/// is collecting. When it is the last one in, moves the night to ranking and emails everyone.
/// </summary>
[UsedImplicitly]
internal class SubmitSuggestionService : IApiGatewayHandler
{
    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        Task.FromResult(ProxyResponseBuilder.Build(HttpStatusCode.NotImplemented));
}

/// <summary>
/// POST /api/nights/{nightId}/ranking. Records this participant's ranking of the other
/// suggestions. When it is the last one in, scores with <see cref="BordaScorer"/>, moves the
/// night to done and emails the results.
/// </summary>
[UsedImplicitly]
internal class SubmitRankingService : IApiGatewayHandler
{
    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        Task.FromResult(ProxyResponseBuilder.Build(HttpStatusCode.NotImplemented));
}

/// <summary>
/// POST /api/nights/{nightId}/advance. Organizer only. Closes the current round without waiting
/// for whoever hasn't answered, so one person can't stall the night.
/// </summary>
[UsedImplicitly]
internal class AdvanceNightService : IApiGatewayHandler
{
    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        Task.FromResult(ProxyResponseBuilder.Build(HttpStatusCode.NotImplemented));
}
