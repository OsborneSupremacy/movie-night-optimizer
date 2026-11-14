using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace MovieNight.Library.Providers;

/// <summary>Where the TMDB read access token comes from. Empty means there isn't one to be had.</summary>
public interface ITmdbTokenSource
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the TMDB read access token from Secrets Manager once per Lambda instance and keeps it.
/// </summary>
/// <remarks>
/// Kept, so that only an instance's first lookup pays for the read, and a Secrets Manager outage
/// after that changes nothing. A failure isn't kept: the next lookup tries again.
/// </remarks>
[UsedImplicitly]
internal class TmdbTokenSource : ITmdbTokenSource
{
    private readonly IAmazonSecretsManager _secretsManager;

    private readonly ILogger<TmdbTokenSource> _logger;

    private readonly string _secretId;

    private string _token = string.Empty;

    // ReSharper disable once ConvertToPrimaryConstructor
    public TmdbTokenSource(IAmazonSecretsManager secretsManager, ILogger<TmdbTokenSource> logger)
    {
        _secretsManager = secretsManager ?? throw new ArgumentNullException(nameof(secretsManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _secretId = EnvReader.TryGetStringValue("TMDB_SECRET_ID", out var secretId)
            ? secretId ?? string.Empty
            : string.Empty;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if(_token.Length > 0 || _secretId.Length == 0)
            return _token;

        try
        {
            var response = await _secretsManager
                .GetSecretValueAsync(new GetSecretValueRequest { SecretId = _secretId }, cancellationToken)
                .ConfigureAwait(false);

            using var secret = JsonDocument.Parse(response.SecretString ?? "{}");

            var token = secret.RootElement.TryGetProperty("apiReadAccessToken", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : string.Empty;

            if(token.Length == 0)
                _logger.LogWarning("The TMDB secret has no apiReadAccessToken, so movies won't be looked up");

            _token = token;
            return _token;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Couldn't read the TMDB secret, so movies won't be looked up");
            return string.Empty;
        }
    }
}
