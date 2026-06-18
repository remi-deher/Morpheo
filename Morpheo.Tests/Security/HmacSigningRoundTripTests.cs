using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Morpheo.Core.Security;
using Morpheo.Sdk;

namespace Morpheo.Tests.Security;

/// <summary>
/// Verifies that a request signed by the client-side <see cref="HmacSigningHandler"/>
/// is accepted by the server-side <see cref="PskHmacAuthenticator"/> — i.e. both sides
/// build the exact same canonical string.
/// </summary>
public class HmacSigningRoundTripTests
{
    private const string Secret = "shared-cluster-secret";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static async Task<HttpContext> SignAndConvertAsync(HttpRequestMessage request)
    {
        var options = new MorpheoOptions { ClusterSecret = Secret };
        var handler = new HmacSigningHandler(options) { InnerHandler = new CapturingHandler() };
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(request, CancellationToken.None);

        // Translate the outgoing HttpRequestMessage into an inbound HttpContext.
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = request.Method.Method;
        ctx.Request.Path = request.RequestUri!.AbsolutePath;
        ctx.Request.QueryString = new QueryString(request.RequestUri.Query);

        var body = request.Content != null ? await request.Content.ReadAsStringAsync() : string.Empty;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        foreach (var h in new[] { "X-Morpheo-Signature", "X-Morpheo-Timestamp", "X-Morpheo-Nonce" })
        {
            if (request.Headers.TryGetValues(h, out var values))
                ctx.Request.Headers[h] = values.First();
        }

        return ctx;
    }

    [Fact]
    public async Task SignedPost_ShouldBeAcceptedByAuthenticator()
    {
        var dto = new SyncLogDto("id", "ent", "Type", "{\"x\":1}", "UPDATE", 123, new(), "node");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://peer:5555/api/sync")
        {
            Content = JsonContent.Create(dto)
        };

        var ctx = await SignAndConvertAsync(request);
        var auth = new PskHmacAuthenticator(Secret, requireFreshness: true);

        (await auth.IsAuthorizedAsync(ctx)).Should().BeTrue();
    }

    [Fact]
    public async Task SignedGet_ShouldBeAcceptedByAuthenticator()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://peer:5555/api/sync/history?since=42&limit=100");

        var ctx = await SignAndConvertAsync(request);
        var auth = new PskHmacAuthenticator(Secret, requireFreshness: true);

        (await auth.IsAuthorizedAsync(ctx)).Should().BeTrue();
    }

    [Fact]
    public async Task TamperedBody_ShouldBeRejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://peer:5555/api/sync")
        {
            Content = new StringContent("{\"x\":1}", Encoding.UTF8, "application/json")
        };

        var ctx = await SignAndConvertAsync(request);
        // Attacker swaps the body after the signature was computed.
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"x\":999}"));

        var auth = new PskHmacAuthenticator(Secret, requireFreshness: true);
        (await auth.IsAuthorizedAsync(ctx)).Should().BeFalse();
    }
}
