using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Strategy that pushes synchronization logs to a central server via HTTP.
/// </summary>
public class HttpPushStrategy : ISyncStrategyProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri _targetUri;
    private readonly ILogger<HttpPushStrategy> _logger;

    public HttpPushStrategy(
        IHttpClientFactory httpClientFactory,
        Uri targetUri,
        ILogger<HttpPushStrategy> logger)
    {
        _httpClientFactory = httpClientFactory;
        _targetUri = targetUri;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("MorpheoCloud");
            
            // Build the full URL.
            // Assuming provided URL is the root (e.g., https://api.morpheo.cloud)
            // Adding the standard Morpheo API path.
            var endpoint = new Uri(_targetUri, "/morpheo/sync/push");

            var response = await client.PostAsJsonAsync(endpoint, log);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            // Fire and forget: log but do not block
            _logger.LogError(ex, "HTTP Push failure to {TargetUri}", _targetUri);
        }
    }
}
