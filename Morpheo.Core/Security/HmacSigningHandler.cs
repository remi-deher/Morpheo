using System.Security.Cryptography;
using System.Text;
using Morpheo.Sdk;

namespace Morpheo.Core.Security;

/// <summary>
/// Client-side counterpart of <see cref="PskHmacAuthenticator"/>. Signs every outgoing
/// inter-node request with the cluster secret so the receiver can authenticate it.
///
/// <para>
/// For each request with a body it sets three headers:
/// <list type="bullet">
///   <item><c>X-Morpheo-Timestamp</c> — unix milliseconds, for freshness.</item>
///   <item><c>X-Morpheo-Nonce</c> — a random value, for replay rejection.</item>
///   <item><c>X-Morpheo-Signature</c> — HMAC-SHA256 over <c>{timestamp}.{nonce}.{body}</c>, hex-encoded.</item>
/// </list>
/// </para>
///
/// <para>
/// If no cluster secret is configured the handler is a transparent pass-through. It must
/// run <b>before</b> any request-compression handler so the signature covers the plaintext
/// body — the same bytes the server sees after decompression.
/// </para>
/// </summary>
public sealed class HmacSigningHandler : DelegatingHandler
{
    private readonly MorpheoOptions _options;

    public HmacSigningHandler(MorpheoOptions options)
    {
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var secret = _options.ClusterSecret;
        if (!string.IsNullOrEmpty(secret))
        {
            // Empty body for GET / body-less requests — the method + path still get signed.
            var body = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            // Must mirror PskHmacAuthenticator's "v2" canonical exactly.
            var pathAndQuery = request.RequestUri?.PathAndQuery ?? "/";
            var canonical = $"{request.Method.Method}\n{pathAndQuery}\n{timestamp}\n{nonce}\n{body}";

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

            request.Headers.TryAddWithoutValidation("X-Morpheo-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-Morpheo-Nonce", nonce);
            request.Headers.TryAddWithoutValidation("X-Morpheo-Signature", signature);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
