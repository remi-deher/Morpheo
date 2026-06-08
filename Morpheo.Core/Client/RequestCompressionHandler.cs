using System.IO.Compression;
using System.Net.Http.Headers;

namespace Morpheo.Core.Client;

/// <summary>
/// A <see cref="DelegatingHandler"/> that gzip-compresses outgoing request bodies
/// once they exceed a size threshold, and advertises gzip/deflate support so the
/// peer may compress its responses in return.
///
/// The server side decompresses transparently via <c>UseRequestDecompression()</c>.
/// Small bodies are left untouched — compression only pays off past a few hundred bytes.
/// </summary>
public sealed class RequestCompressionHandler : DelegatingHandler
{
    /// <summary>
    /// Bodies smaller than this (in bytes) are sent uncompressed — the CPU and the
    /// gzip header overhead are not worth it for tiny single-log pushes.
    /// </summary>
    public const int CompressionThresholdBytes = 1024;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Tell the peer we accept compressed responses (Cold Sync history can be large).
        if (!request.Headers.AcceptEncoding.Any())
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        }

        if (request.Content != null && !request.Content.Headers.ContentEncoding.Contains("gzip"))
        {
            var original = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (original.Length >= CompressionThresholdBytes)
            {
                request.Content = BuildGzipContent(original, request.Content.Headers.ContentType);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static ByteArrayContent BuildGzipContent(byte[] payload, MediaTypeHeaderValue? contentType)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(payload, 0, payload.Length);
        }

        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = contentType;
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }
}
