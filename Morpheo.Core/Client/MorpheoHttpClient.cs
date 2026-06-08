using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Client;

/// <summary>
/// HTTP client implementation for communicating with Morpheo nodes.
/// </summary>
public class MorpheoHttpClient : IMorpheoClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MorpheoHttpClient> _logger;
    private readonly MorpheoOptions _options;

    public MorpheoHttpClient(
        IHttpClientFactory httpClientFactory,
        ILogger<MorpheoHttpClient> logger,
        MorpheoOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options;
    }

    private string BuildUrl(PeerInfo target, string path)
    {
        // Respect security choice (HTTP vs HTTPS)
        string scheme = _options.UseSecureConnection ? "https" : "http";

        var address = target.IpAddress;
        if (address.Contains(":") && !address.Contains("["))
        {
            address = $"[{address}]";
        }

        return $"{scheme}://{address}:{target.Port}{path}";
    }

    /// <inheritdoc/>
    public async Task SendPrintJobAsync(PeerInfo target, string content)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var url = BuildUrl(target, "/api/print");

            var request = new { Content = content, Sender = _options.NodeName };
            await client.PostAsJsonAsync(url, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Print Error to {PeerName}", target.Name);
        }
    }

    /// <inheritdoc/>
    public async Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            var url = BuildUrl(target, "/api/sync");

            var response = await client.PostAsJsonAsync(url, log);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Sync Push Error to {target.Name}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sync failed to {PeerName} (Expected if disconnected)", target.Name);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SendSyncBatchAsync(PeerInfo target, IReadOnlyList<SyncLogDto> logs)
    {
        if (logs == null || logs.Count == 0) return true;

        try
        {
            var client = _httpClientFactory.CreateClient();
            // Scale the timeout modestly with batch size so large catch-ups are not cut off.
            client.Timeout = TimeSpan.FromSeconds(Math.Min(30, 2 + logs.Count / 100.0));
            var url = BuildUrl(target, "/api/sync/batch");

            var response = await client.PostAsJsonAsync(url, logs);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sync Batch Error to {PeerName}: {StatusCode}", target.Name, response.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sync batch failed to {PeerName} (Expected if disconnected)", target.Name);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick, int limit = 500)
    {
        var url = BuildUrl(target, $"/api/sync/history?since={sinceTick}&limit={limit}");
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            return await client.GetFromJsonAsync<List<SyncLogDto>>(url) ?? new List<SyncLogDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cold Sync Failed to {PeerName}", target.Name);
            return new List<SyncLogDto>();
        }
    }
}
