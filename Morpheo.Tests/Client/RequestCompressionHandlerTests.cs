using System.IO.Compression;
using System.Net;
using System.Text;
using FluentAssertions;
using Morpheo.Core.Client;

namespace Morpheo.Tests.Client;

public class RequestCompressionHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> SendAsync(string body)
    {
        var inner = new CapturingHandler();
        var handler = new RequestCompressionHandler { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://x/api/sync")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        await invoker.SendAsync(request, CancellationToken.None);
        return inner.Captured!;
    }

    [Fact]
    public async Task SmallBody_ShouldNotBeCompressed()
    {
        var captured = await SendAsync("{\"x\":1}");
        captured.Content!.Headers.ContentEncoding.Should().NotContain("gzip");
    }

    [Fact]
    public async Task LargeBody_ShouldBeGzipped_AndRoundTrip()
    {
        var large = "{\"data\":\"" + new string('A', 5000) + "\"}";
        var captured = await SendAsync(large);

        captured.Content!.Headers.ContentEncoding.Should().Contain("gzip");

        // Decompress and confirm we recover the original payload.
        var compressed = await captured.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(compressed);
        using var gzip = new GZipStream(ms, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var restored = await reader.ReadToEndAsync();

        restored.Should().Be(large);
    }

    [Fact]
    public async Task ShouldAdvertiseAcceptEncoding()
    {
        var captured = await SendAsync("{\"x\":1}");
        captured.Headers.AcceptEncoding.Select(e => e.Value).Should().Contain("gzip");
    }
}
