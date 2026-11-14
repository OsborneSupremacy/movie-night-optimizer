using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace MovieNight.Library.Handlers;

/// <summary>
/// The one Lambda behind every API route. Dispatches on the HTTP API route key to a keyed
/// handler, so one deployment artifact serves every endpoint.
/// </summary>
[UsedImplicitly]
public class Router
{
    private IServiceProvider? _serviceProvider;
    private readonly Lock _serviceProviderLock = new();

    public Router() { }

    protected Router(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    private IServiceProvider GetServiceProvider()
    {
        if(_serviceProvider is not null) return _serviceProvider;
        using (_serviceProviderLock.EnterScope())
        {
            if(_serviceProvider is not null) return _serviceProvider;
            _serviceProvider = ServiceProviderBuilder.Build();
        }
        return _serviceProvider;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    )
    {
        // "POST /api/nights/{nightId}" becomes "post /api/nights/{nightid}".
        var serviceKey = (request.RouteKey ?? string.Empty).ToLowerInvariant();

        var service = GetServiceProvider()
            .GetKeyedService<IApiGatewayHandler>(serviceKey);

        if (service is not null)
            return await service.FunctionHandler(request, context);

        context.Logger.LogError($"Couldn't find api gateway handler for {serviceKey}");
        return ProxyResponseBuilder.Build(HttpStatusCode.NotFound);
    }
}
