namespace MovieNight.Library.Abstractions;

public interface IApiGatewayHandler
{
    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    );
}
