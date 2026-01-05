using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MessagePack;
using Morpheo.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Core.Sdk;

namespace Morpheo.Core.Client;

/// <summary>
/// HTTP transport layer for inter-node communication with MessagePack optimization.
/// Falls back to JSON when remote peer doesn't support binary serialization.
/// </summary>
public class MorpheoHttpClient : IMorpheoClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MorpheoHttpClient> _logger;
    private readonly MorpheoOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public MorpheoHttpClient(
        IHttpClientFactory httpClientFactory,
        ILogger<MorpheoHttpClient> logger,
        MorpheoOptions options,
        IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options;
        _serviceProvider = serviceProvider;
    }

    private string BuildUrl(PeerInfo target, string path)
    {
        string scheme = _options.UseSecureConnection ? "https" : "http";

        var address = target.IpAddress;
        if (address.Contains(":") && !address.Contains("["))
        {
            address = $"[{address}]";
        }

        return $"{scheme}://{address}:{target.Port}{path}";
    }

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
            _logger.LogError($"Print Error to {target.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends sync update with automatic MessagePack->JSON fallback for legacy peer compatibility.
    /// WARNING: UnsupportedMediaType from remote triggers permanent downgrade to avoid retry storms.
    /// </summary>
    public async Task SendSyncUpdateAsync(PeerInfo target, SyncLogDto log)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            var url = BuildUrl(target, "/api/sync");

            try 
            {
                var bytes = MessagePackSerializer.Serialize(log);
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-msgpack");

                var response = await client.PostAsync(url, content);
                if (response.StatusCode == System.Net.HttpStatusCode.UnsupportedMediaType)
                {
                    await SendJsonFallbackAsync(client, url, log, target);
                    return;
                }
                
                if (!response.IsSuccessStatusCode)
                {
                     _logger.LogWarning($"Sync Push Error (MsgPack) to {target.Name}: {response.StatusCode}");
                }
                return;
            }
            catch (Exception ex)
            {
                 _logger.LogWarning($"MessagePack serialization/send failed, retrying with JSON: {ex.Message}");
                 await SendJsonFallbackAsync(client, url, log, target);
            }
        }
        catch (Exception)
        {
            _logger.LogDebug($"Sync failed to {target.Name} (Expected if disconnected)");
        }
    }

    private async Task SendJsonFallbackAsync(HttpClient client, string url, SyncLogDto log, PeerInfo target)
    {
        var response = await client.PostAsJsonAsync(url, log);
        if (!response.IsSuccessStatusCode)
        {
             _logger.LogWarning($"Sync Push Error (JSON) to {target.Name}: {response.StatusCode}");
        }
    }

    /// <summary>
    /// Fetches incremental sync history since a given timestamp (cold-sync bootstrap).
    /// </summary>
    public async Task<List<SyncLogDto>> GetHistoryAsync(PeerInfo target, long sinceTick)
    {
        var url = BuildUrl(target, $"/api/sync/history?since={sinceTick}");
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            return await client.GetFromJsonAsync<List<SyncLogDto>>(url) ?? new List<SyncLogDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Cold Sync Failed to {target.Name}: {ex.Message}");
            return new List<SyncLogDto>();
        }
    }


    public async Task<List<SyncLogDto>> GetHistoryByRangeAsync(PeerInfo target, long startTick, long endTick)
    {
        var url = BuildUrl(target, $"/morpheo/sync/history/{startTick}/{endTick}");
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            return await client.GetFromJsonAsync<List<SyncLogDto>>(url) ?? new List<SyncLogDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetHistoryByRange Failed to {target.Name}: {ex.Message}");
            return new List<SyncLogDto>();
        }
    }

    public async Task<MerkleTreeNode?> GetMerkleRootAsync(PeerInfo target)
    {
        var url = BuildUrl(target, "/morpheo/sync/merkle/root");
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            return await client.GetFromJsonAsync<MerkleTreeNode>(url);
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetMerkleRoot Failed to {target.Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<MerkleTreeNode>> GetMerkleChildrenAsync(PeerInfo target, string nodeHash)
    {
        var url = BuildUrl(target, $"/morpheo/sync/merkle/children/{nodeHash}");
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            return await client.GetFromJsonAsync<List<MerkleTreeNode>>(url) ?? new List<MerkleTreeNode>();
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetMerkleChildren Failed to {target.Name}: {ex.Message}");
            return new List<MerkleTreeNode>();
        }
    }

    /// <summary>
    /// Factory for generic MorpheoSet instances without polluting DI with every T variant.
    /// Uses ActivatorUtilities to inject DbContextFactory and DataSyncService at runtime.
    /// </summary>
    public IMorpheoSet<T> Set<T>() where T : MorpheoEntity
    {
        return ActivatorUtilities.CreateInstance<MorpheoSet<T>>(_serviceProvider);
    }
}
