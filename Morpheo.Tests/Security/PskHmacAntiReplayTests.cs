using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Morpheo.Core.Security;

namespace Morpheo.Tests.Security;

/// <summary>
/// Covers the "v2" signing scheme: HMAC over method + path + timestamp + nonce + body,
/// with freshness validation and single-use nonces (anti-replay).
/// </summary>
public class PskHmacAntiReplayTests
{
    private const string Key = "cluster-secret";

    private static string SignV2(string method, string pathAndQuery, string timestamp, string nonce, string body)
    {
        var canonical = $"{method}\n{pathAndQuery}\n{timestamp}\n{nonce}\n{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static DefaultHttpContext BuildContext(
        string method, string path, string query, string body,
        string timestamp, string nonce, string signature)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.QueryString = new QueryString(query);
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.Headers["X-Morpheo-Signature"] = signature;
        ctx.Request.Headers["X-Morpheo-Timestamp"] = timestamp;
        ctx.Request.Headers["X-Morpheo-Nonce"] = nonce;
        return ctx;
    }

    [Fact]
    public async Task FreshSignedRequest_ShouldBeAuthorized()
    {
        var auth = new PskHmacAuthenticator(Key, requireFreshness: true);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = "nonce-1";
        var body = "{\"data\":1}";
        var sig = SignV2("POST", "/api/sync", ts, nonce, body);

        var ctx = BuildContext("POST", "/api/sync", "", body, ts, nonce, sig);

        (await auth.IsAuthorizedAsync(ctx)).Should().BeTrue();
    }

    [Fact]
    public async Task ReplayedNonce_ShouldBeRejected_OnSecondUse()
    {
        var auth = new PskHmacAuthenticator(Key, requireFreshness: true);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = "replay-me";
        var body = "{\"data\":1}";
        var sig = SignV2("POST", "/api/sync", ts, nonce, body);

        // First use succeeds and commits the nonce.
        var ctx1 = BuildContext("POST", "/api/sync", "", body, ts, nonce, sig);
        (await auth.IsAuthorizedAsync(ctx1)).Should().BeTrue();

        // Replaying the identical request must now fail.
        var ctx2 = BuildContext("POST", "/api/sync", "", body, ts, nonce, sig);
        (await auth.IsAuthorizedAsync(ctx2)).Should().BeFalse();
    }

    [Fact]
    public async Task StaleTimestamp_ShouldBeRejected()
    {
        var auth = new PskHmacAuthenticator(Key, TimeSpan.FromMinutes(5), requireFreshness: true);
        var staleTs = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds().ToString();
        var nonce = "old";
        var body = "{}";
        var sig = SignV2("POST", "/api/sync", staleTs, nonce, body);

        var ctx = BuildContext("POST", "/api/sync", "", body, staleTs, nonce, sig);

        (await auth.IsAuthorizedAsync(ctx)).Should().BeFalse();
    }

    [Fact]
    public async Task MissingFreshnessHeaders_ShouldBeRejected_WhenRequired()
    {
        var auth = new PskHmacAuthenticator(Key, requireFreshness: true);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/sync";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        // Only a body-only signature, no timestamp/nonce.
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        ctx.Request.Headers["X-Morpheo-Signature"] =
            Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("{}")));

        (await auth.IsAuthorizedAsync(ctx)).Should().BeFalse();
    }

    [Fact]
    public async Task SignedGetRequest_WithEmptyBody_ShouldBeAuthorized()
    {
        var auth = new PskHmacAuthenticator(Key, requireFreshness: true);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = "get-nonce";
        var sig = SignV2("GET", "/api/sync/history?since=0&limit=500", ts, nonce, "");

        var ctx = BuildContext("GET", "/api/sync/history", "?since=0&limit=500", "", ts, nonce, sig);

        (await auth.IsAuthorizedAsync(ctx)).Should().BeTrue();
    }
}
