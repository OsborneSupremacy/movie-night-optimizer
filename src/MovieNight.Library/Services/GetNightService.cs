namespace MovieNight.Library.Services;

/// <summary>
/// GET /api/nights/{nightId}. What this participant should see: the phase, whether they have
/// answered yet, the suggestions they can rank (never their own), and the results once done.
/// </summary>
/// <remarks>
/// Only reads. Mail scanners follow the links in emails, so opening one must never change
/// anything.
/// </remarks>
[UsedImplicitly]
internal class GetNightService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    private readonly NightReader _nightReader;

    public GetNightService(ApiGatewayAdapter adapter, NightReader nightReader)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _nightReader = nightReader ?? throw new ArgumentNullException(nameof(nightReader));
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

    internal Task<Result<NightView>> ExecuteAsync(NightRequest request) =>
        _nightReader.ViewAsync(request.NightId, request.Token);
}
