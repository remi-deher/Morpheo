using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;
using Morpheo.Sdk.Blobs;
using Morpheo.Core.Data;


namespace Morpheo.Core.Sync;

/// <summary>
/// Background service that listens for incoming data changes, detects Blob references,
/// and downloads missing binary content from peers.
/// </summary>
public class BlobSyncService : IHostedService
{
    private readonly DataSyncService _dataSync;
    private readonly IMorpheoBlobStore _blobStore;
    private readonly INetworkDiscovery _discovery;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlobSyncService> _logger;

    // Regex to detect "BlobId": "..." or "blobId": "..." pattern in JSON
    // We handle standard GUIDs or string IDs.
    private static readonly Regex BlobIdRegex = new Regex("\"[Bb]lobId\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    public BlobSyncService(
        DataSyncService dataSync,
        IMorpheoBlobStore blobStore,
        INetworkDiscovery discovery,
        IHttpClientFactory httpClientFactory,
        ILogger<BlobSyncService> logger)
    {
        _dataSync = dataSync;
        _blobStore = blobStore;
        _discovery = discovery;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _dataSync.LogAdded += OnDataReceived;
        _logger.LogInformation("BlobSyncService started listening for sync events.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _dataSync.LogAdded -= OnDataReceived;
        return Task.CompletedTask;
    }

    private void OnDataReceived(object? sender, SyncLog log)
    {
        // Fire and Forget execution to avoid blocking the sync loop
        _ = Task.Run(async () => await ProcessLogAsync(log));
    }

    private async Task ProcessLogAsync(SyncLog log)
    {
        try
        {
            if (string.IsNullOrEmpty(log.JsonData)) return;

            // 1. Detect BlobId in JSON
            var match = BlobIdRegex.Match(log.JsonData);
            if (!match.Success) return;

            var blobId = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(blobId)) return;

            // 2. Check if we already have it
            var metadata = await _blobStore.GetBlobMetadataAsync(blobId);
            if (metadata != null) return; // Already exists

            // 3. Find source peer
            // We need the peer corresponding to the OriginNodeId (initially creating the blob)
            // or we could try to download from the node that just sent us the log.
            // But SyncLog entity doesn't strictly store the "Sender" of the message, it stores "OriginNodeId" (creator).
            // In a Mesh, we might receive the log from an intermediary.
            // For simplicity in this iteration, we try to find the OriginNode.
            
            // Note: In Gossip, 'log.IsFromRemote' is true. 'OriginNodeId' is inside the log entity but not exposed in SyncLog class directly (it's in DTO).
            // Wait, SyncLog entity definition does NOT have OriginNodeId in the file I viewed earlier! 
            // It has EntityId, EntityName, etc.
            // Let's assume we can try to find ANY peer that has it, or valid peer.
            // Without Sender info in SyncLog, we have to guess or query.
            // However, the PROMPT suggested: "from the peer that emitted it (PeerInfo)".
            // But DataSyncService event only passes the `SyncLog` entity which is stored in DB.
            // Limitation: We lost the "Sender" info when saving to DB.
            
            // Strategy: Try to find a peer by name matches or just pick a random peer (Gossip style).
            // Better: Iterate over known peers and try to fetch.
            
            var peers = _discovery.GetPeers();
            if (peers.Count == 0) return;

            // Try to find if Origin is known, else try all/random.
            // Since we don't have OriginId easily accessible without parsing JSON or adding field, 
            // let's try a random peer or the first one, assuming small cluster or propagation.
            // Ideally we should have passed the Source Peer in the event.
            
            foreach(var peer in peers)
            {
                 if(await TryDownloadBlobAsync(peer, blobId))
                 {
                     return;
                 }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing blob sync for log {log.Id}");
        }
    }

    private async Task<bool> TryDownloadBlobAsync(PeerInfo peer, string blobId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5); // Large timeout for blobs

            // Construct URL: http://{ip}:{port}/morpheo/blobs/{id}
             string scheme = "http"; // Assuming HTTP for now or check options
             var address = peer.IpAddress;
             if (address.Contains(":") && !address.Contains("[")) address = $"[{address}]";
             
             var url = $"{scheme}://{address}:{peer.Port}/morpheo/blobs/{blobId}";
             
             using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
             if (!response.IsSuccessStatusCode) return false;

             var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
             var fileName = response.Content.Headers.ContentDisposition?.FileName ?? $"{blobId}.bin";

             using var stream = await response.Content.ReadAsStreamAsync();
             
             // Save to Store
             await _blobStore.SaveBlobAsync(stream, fileName, contentType);
             
             _logger.LogInformation($"Blob {blobId} downloaded successfully from {peer.Name}.");
             return true;
        }
        catch
        {
            // Silent fail, try next peer
            return false;
        }
    }
}
