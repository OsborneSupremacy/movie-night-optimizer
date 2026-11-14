namespace MovieNight.Library.Builders;

/// <summary>
/// Builds HTTP API (payload format 2.0) responses.
/// </summary>
/// <remarks>
/// No CORS headers. The API is served from the same origin as the site, behind the CloudFront
/// behavior for /api/*, so the browser never makes a cross-origin request.
/// </remarks>
internal static class ProxyResponseBuilder
{
    public static APIGatewayHttpApiV2ProxyResponse Build(HttpStatusCode statusCode) =>
        new()
        {
            StatusCode = (int)statusCode
        };

    public static APIGatewayHttpApiV2ProxyResponse Build(HttpStatusCode statusCode, string body) =>
        Build(statusCode, body, "no-store");

    /// <param name="cacheControl">
    /// What CloudFront and the browser may keep. Every response says, so that nothing is cached by
    /// accident: only a behavior with a caching policy honours anything but "no-store".
    /// </param>
    public static APIGatewayHttpApiV2ProxyResponse Build(HttpStatusCode statusCode, string body, string cacheControl) =>
        new()
        {
            StatusCode = (int)statusCode,
            Body = body,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Cache-Control"] = cacheControl
            }
        };
}
