namespace MovieNight.Library.Extensions;

internal static class ApiGatewayRequestExtensions
{
    /// <summary>
    /// The header a participant's token travels in. A header rather than the query string, so the
    /// token stays out of CloudFront and API Gateway access logs. HTTP API lower-cases header names.
    /// </summary>
    public const string ParticipantTokenHeader = "x-participant-token";

    public static string GetPathParameter(this APIGatewayHttpApiV2ProxyRequest request, string name) =>
        request.PathParameters is not null && request.PathParameters.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    public static string GetParticipantToken(this APIGatewayHttpApiV2ProxyRequest request) =>
        request.Headers is not null && request.Headers.TryGetValue(ParticipantTokenHeader, out var value)
            ? value
            : string.Empty;
}
