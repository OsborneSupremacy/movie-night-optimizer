namespace MovieNight.Library.Services;

/// <summary>
/// Answers without touching DynamoDB or SES, so a 200 here says the domain, CloudFront, the API
/// and the function are wired together and nothing more.
/// </summary>
[UsedImplicitly]
internal class HealthService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    public HealthService(ApiGatewayAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        Task.FromResult(ProxyResponseBuilder.Build(
            HttpStatusCode.OK,
            _adapter.Serialize(new HealthResponse { Status = "ok" })
        ));
}
