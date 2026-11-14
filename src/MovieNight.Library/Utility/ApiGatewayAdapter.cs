namespace MovieNight.Library.Utility;

/// <summary>
/// Boilerplate for adapting an HTTP API request and response to and from a business request and
/// response.
/// </summary>
internal class ApiGatewayAdapter
{
    private readonly ILogger<ApiGatewayAdapter> _logger;

    private readonly JsonSerializerOptions _jsonOptions;

    private readonly IServiceProvider _serviceProvider;

    public ApiGatewayAdapter(
        ILogger<ApiGatewayAdapter> logger,
        JsonSerializerOptions jsonOptions,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Deserializes the request body, validates it and hands it to the handler.
    /// </summary>
    public async Task<APIGatewayHttpApiV2ProxyResponse> AdaptAsync<TRequest, TResponse>(
        APIGatewayHttpApiV2ProxyRequest request,
        Func<TRequest, Task<Result<TResponse>>> handler
    )
    {
        var body = ReadBody<TRequest>(request);

        if(body.IsFaulted)
            return ProxyResponseBuilder.Build(body.StatusCode, Serialize(new ErrorResponse { Message = body.Exception.Message }));

        return await AdaptAsync(body.Value, handler).ConfigureAwait(false);
    }

    /// <summary>
    /// For a request the caller has already assembled, from the path, headers and body together.
    /// </summary>
    public async Task<APIGatewayHttpApiV2ProxyResponse> AdaptAsync<TRequest, TResponse>(
        TRequest innerRequest,
        Func<TRequest, Task<Result<TResponse>>> handler
    )
    {
        var (isValid, validationError) = GetValidationError(innerRequest);

        if(!isValid)
        {
            _logger.LogWarning("Validation failed for {RequestType}: {ValidationError}", typeof(TRequest).Name, validationError);
            return ProxyResponseBuilder.Build(HttpStatusCode.BadRequest, Serialize(new ErrorResponse { Message = validationError }));
        }

        var result = await handler(innerRequest).ConfigureAwait(false);

        if(result.IsFaulted)
            return ProxyResponseBuilder.Build(result.StatusCode, Serialize(new ErrorResponse { Message = result.Exception.Message }));

        if(result.Value is StatusCodeOnlyResponse)
            return ProxyResponseBuilder.Build(result.StatusCode);

        return ProxyResponseBuilder.Build(result.StatusCode, Serialize(result.Value));
    }

    public Result<TBody> ReadBody<TBody>(APIGatewayHttpApiV2ProxyRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Body))
            return new Result<TBody>(new InvalidOperationException("A request body is required."), HttpStatusCode.BadRequest);

        try
        {
            var json = request.IsBase64Encoded
                ? Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
                : request.Body;

            var value = JsonSerializer.Deserialize<TBody>(json, _jsonOptions);

            return value is null
                ? new Result<TBody>(new InvalidOperationException("A request body is required."), HttpStatusCode.BadRequest)
                : new Result<TBody>(value, HttpStatusCode.OK);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return new Result<TBody>(new InvalidOperationException("The request body is not valid JSON."), HttpStatusCode.BadRequest);
        }
    }

    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, _jsonOptions);

    private (bool isValid, string error) GetValidationError<TRequest>(TRequest request)
    {
        var validator = _serviceProvider.GetService<IValidator<TRequest>>();

        if(validator is null)
            return (true, string.Empty);

        var validationResult = validator.Validate(request);

        if(validationResult.IsValid)
            return (true, string.Empty);

        return (false, validationResult.Errors.Select(e => e.ErrorMessage).Aggregate((a, b) => $"{a}; {b}"));
    }
}
