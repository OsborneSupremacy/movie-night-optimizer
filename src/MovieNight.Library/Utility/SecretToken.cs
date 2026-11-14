using System.Security.Cryptography;

namespace MovieNight.Library.Utility;

/// <summary>
/// Unguessable tokens, and the one-way transform they are stored under.
///
/// A token is handed out once, in an emailed link, and never held anywhere afterwards. Only
/// <see cref="Hash"/> of it is, so possession of the table is not possession of the token.
/// Matching one that comes back means hashing what arrived and looking for that instead.
/// </summary>
internal static class SecretToken
{
    /// <summary>256 bits. The token only ever appears in a URL, so nothing is paid for the length.</summary>
    public const int TokenBytes = 32;

    /// <summary>
    /// A new token in the clear. This is the only time it exists outside the email it is sent in;
    /// store <see cref="Hash"/> of it rather than the return value.
    /// </summary>
    /// <remarks>
    /// Base64url rather than plain base64: it yields only letters, digits, '-' and '_', all of
    /// which a URL may carry unencoded, where base64's '+' and '/' may not.
    /// </remarks>
    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Hex-encoded SHA-256, which is what the TokenHash attribute holds.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
