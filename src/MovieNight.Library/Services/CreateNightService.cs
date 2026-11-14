namespace MovieNight.Library.Services;

/// <summary>
/// POST /api/nights. Creates a night in <see cref="NightPhase.Collecting"/> and emails every
/// participant a link to the page where they suggest a movie.
/// </summary>
/// <remarks>
/// TODO:
/// 1. Limit nights per organizer inbox per day before doing anything else. This endpoint sends
///    mail to addresses a stranger typed, from an SES account other applications share.
/// 2. Build the night: a participant id and a SecretToken per person, storing SecretToken.Hash.
/// 3. NightProvider.SaveNewNightAsync.
/// 4. EmailSender.SendAsync to each participant, with the token in the URL fragment:
///    {SITE_URL}/night/{nightId}#t={token}
/// </remarks>
[UsedImplicitly]
internal class CreateNightService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    public CreateNightService(ApiGatewayAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<CreateNightRequest, CreateNightResponse>(request, ExecuteAsync);

    internal Task<Result<CreateNightResponse>> ExecuteAsync(CreateNightRequest request) =>
        Task.FromResult(new Result<CreateNightResponse>(
            new NotImplementedException("Creating a night isn't built yet."),
            HttpStatusCode.NotImplemented
        ));
}
