using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Morpheo.Sdk;

namespace Morpheo.Core.Security;

/// <summary>
/// "Opt-in" secure authenticator.
/// Verifies that the request is signed with a shared secret key (Cluster Secret).
/// Complies with Morpheo RFC Section 6.
/// </summary>
public class PskHmacAuthenticator : IRequestAuthenticator
{
    private readonly byte[] _secretKey;
    private const string HEADER_NAME = "X-Morpheo-Signature";

    public PskHmacAuthenticator(string preSharedKey)
    {
        if (string.IsNullOrWhiteSpace(preSharedKey))
            throw new ArgumentNullException(nameof(preSharedKey));
        _secretKey = Encoding.UTF8.GetBytes(preSharedKey);
    }

    public async Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        // 1. Header present?
        if (!context.Request.Headers.TryGetValue(HEADER_NAME, out var receivedSignature)
            || string.IsNullOrEmpty(receivedSignature))
        {
            return false;
        }

        // 2. Allow rewinding the Body so downstream handlers can still read it.
        context.Request.EnableBuffering();

        // 3. Read body
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var bodyContent = await reader.ReadToEndAsync();

        // Rewind for the rest of the pipeline
        context.Request.Body.Position = 0;

        // 4. Compute HMAC-SHA256
        using var hmac = new HMACSHA256(_secretKey);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(bodyContent));

        // 5. Parse received signature to bytes
        byte[]? receivedBytes = null;
        try
        {
            receivedBytes = Convert.FromHexString(receivedSignature.ToString());
        }
        catch (FormatException)
        {
            return false;
        }

        // 6. Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(computedHash, receivedBytes);
    }
}
