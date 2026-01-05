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
        _secretKey = Encoding.UTF8.GetBytes(preSharedKey);
    }

    public async Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        // 1. Header present?
        if (!context.Request.Headers.TryGetValue(HEADER_NAME, out var receivedSignature))
        {
            return false;
        }

        // 2. IMPORTANT: Allow rewinding the Body
        // Otherwise the API Controller won't be able to read the JSON after us.
        context.Request.EnableBuffering();

        // 3. Read content
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var bodyContent = await reader.ReadToEndAsync();

        // Rewind the stream for the pipeline
        context.Request.Body.Position = 0;

        // 4. Compute Hash
        using var hmac = new HMACSHA256(_secretKey);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(bodyContent));
        var computedSignature = Convert.ToHexString(computedHash);

        // 5. Comparison (Case Insensitive)
        return string.Equals(computedSignature, receivedSignature, StringComparison.OrdinalIgnoreCase);
    }
}
