using System.Security.Cryptography;

namespace BlogIt.Shared.Helpers;

/// <summary>Generates the HMAC signing secret for BlogIt's JWTs.</summary>
public static class JwtSecretGenerator
{
    /// <summary>
    /// 256 bits — double the 128 bits HS256 requires, and the same width as the SHA-256 output it
    /// keys, so no length is wasted and none is short.
    /// </summary>
    private const int SecretBytes = 32;

    /// <summary>
    /// A fresh base64 secret from the OS cryptographic RNG. Never callable from a request body:
    /// the secret is generated here and stored, and is not echoed back to any client.
    /// </summary>
    /// <remarks>
    /// Concatenated <c>Guid.NewGuid()</c> values used to fill this role. They are long enough to
    /// look adequate and carry fixed version and variant bits, but .NET does not document GUID
    /// generation as cryptographically secure — which makes it the wrong primitive for a value
    /// whose only job is to be unguessable.
    /// </remarks>
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
}
