using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Morpheo.Sdk;

namespace Morpheo.Core.Security;

/// <summary>
/// "Opt-in" secure authenticator.
/// Verifies that the request is signed with a shared secret key (Cluster Secret).
/// Complies with Morpheo RFC Section 6.
///
/// <para>
/// In addition to body integrity (HMAC over the payload), this authenticator supports
/// <b>anti-replay</b> protection: when the request carries <c>X-Morpheo-Timestamp</c> and
/// <c>X-Morpheo-Nonce</c> headers, the signature is verified over
/// <c>{timestamp}.{nonce}.{body}</c>, the timestamp must be within
/// <see cref="_clockSkew"/> of now, and each nonce may be used only once within that
/// window. A captured-and-replayed request is therefore rejected.
/// </para>
/// </summary>
public class PskHmacAuthenticator : IRequestAuthenticator
{
    private readonly byte[] _secretKey;
    private readonly TimeSpan _clockSkew;
    private readonly bool _requireFreshness;

    private const string HEADER_SIGNATURE = "X-Morpheo-Signature";
    private const string HEADER_TIMESTAMP = "X-Morpheo-Timestamp";
    private const string HEADER_NONCE     = "X-Morpheo-Nonce";

    // Seen nonce -> UTC expiry tick. Bounded by pruning expired entries.
    private readonly ConcurrentDictionary<string, long> _seenNonces = new();

    /// <param name="preSharedKey">The cluster shared secret.</param>
    /// <param name="clockSkew">Maximum accepted difference between sender and receiver clocks. Default 5 minutes.</param>
    /// <param name="requireFreshness">
    /// When true, requests MUST carry timestamp + nonce headers (anti-replay mandatory).
    /// When false (default), body-only signatures are still accepted for backward compatibility,
    /// but freshness is enforced whenever those headers are present.
    /// </param>
    public PskHmacAuthenticator(string preSharedKey, TimeSpan? clockSkew = null, bool requireFreshness = false)
    {
        if (string.IsNullOrWhiteSpace(preSharedKey))
            throw new ArgumentNullException(nameof(preSharedKey));

        _secretKey = Encoding.UTF8.GetBytes(preSharedKey);
        _clockSkew = clockSkew ?? TimeSpan.FromMinutes(5);
        _requireFreshness = requireFreshness;
    }

    public async Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        // 1. Signature header present?
        if (!context.Request.Headers.TryGetValue(HEADER_SIGNATURE, out var receivedSignature)
            || string.IsNullOrEmpty(receivedSignature))
        {
            return false;
        }

        // 2. Optional freshness headers
        var timestamp = context.Request.Headers.TryGetValue(HEADER_TIMESTAMP, out var tsVal) ? tsVal.ToString() : null;
        var nonce     = context.Request.Headers.TryGetValue(HEADER_NONCE, out var nVal) ? nVal.ToString() : null;
        bool hasFreshness = !string.IsNullOrEmpty(timestamp) && !string.IsNullOrEmpty(nonce);

        if (_requireFreshness && !hasFreshness)
            return false;

        if (hasFreshness && !ValidateFreshness(timestamp!, nonce!))
            return false;

        // 3. Allow rewinding the Body so downstream handlers can still read it.
        context.Request.EnableBuffering();

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var bodyContent = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        // 4. Canonical payload. The freshness ("v2") scheme binds the signature to the
        //    HTTP method and path as well, so it also protects body-less GET requests
        //    (e.g. Cold Sync history pulls). Legacy body-only signatures remain accepted.
        string canonical;
        if (hasFreshness)
        {
            var pathAndQuery = context.Request.Path + context.Request.QueryString;
            canonical = $"{context.Request.Method}\n{pathAndQuery}\n{timestamp}\n{nonce}\n{bodyContent}";
        }
        else
        {
            canonical = bodyContent;
        }

        // 5. Compute HMAC-SHA256
        using var hmac = new HMACSHA256(_secretKey);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));

        // 6. Parse received signature to bytes
        byte[] receivedBytes;
        try
        {
            receivedBytes = Convert.FromHexString(receivedSignature.ToString());
        }
        catch (FormatException)
        {
            return false;
        }

        // 7. Constant-time comparison to prevent timing attacks
        if (!CryptographicOperations.FixedTimeEquals(computedHash, receivedBytes))
            return false;

        // 8. Signature valid — commit the nonce so it cannot be replayed.
        if (hasFreshness)
            CommitNonce(nonce!);

        return true;
    }

    private bool ValidateFreshness(string timestamp, string nonce)
    {
        // Timestamp is unix milliseconds.
        if (!long.TryParse(timestamp, out var unixMs))
            return false;

        var sent = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        var delta = (DateTimeOffset.UtcNow - sent).Duration();
        if (delta > _clockSkew)
            return false;

        PruneExpiredNonces();

        // Reject if this nonce was already used within the window.
        return !_seenNonces.ContainsKey(nonce);
    }

    private void CommitNonce(string nonce)
    {
        var expiry = DateTime.UtcNow.Add(_clockSkew).Ticks;
        _seenNonces[nonce] = expiry;
    }

    private void PruneExpiredNonces()
    {
        if (_seenNonces.IsEmpty) return;

        var now = DateTime.UtcNow.Ticks;
        foreach (var kvp in _seenNonces)
        {
            if (kvp.Value < now)
                _seenNonces.TryRemove(kvp.Key, out _);
        }
    }
}
