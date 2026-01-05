using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync;

/// <summary>
/// Background service responsible for Anti-Entropy (repairing inconsistencies).
/// Periodically synchronizes with a random peer to ensure eventual consistency.
/// </summary>
public class AntiEntropyService : BackgroundService
{
    private readonly DataSyncService _syncService;
    private readonly INetworkDiscovery _discovery;
    private readonly ILogger<AntiEntropyService> _logger;
    private readonly MerkleTreeService _merkleService;
    private readonly IMorpheoClient _client;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30); 

    public AntiEntropyService(
        DataSyncService syncService,
        INetworkDiscovery discovery,
        ILogger<AntiEntropyService> logger,
        MerkleTreeService merkleService,
        IMorpheoClient client)
    {
        _syncService = syncService;
        _discovery = discovery;
        _logger = logger;
        _merkleService = merkleService;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // MERKLE INIT
        await _merkleService.InitializeAsync();

        _logger.LogInformation("Anti-Entropy Service started.");

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformAntiEntropyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Anti-Entropy cycle.");
            }
        }
    }

    private async Task PerformAntiEntropyAsync()
    {
        var peers = _discovery.GetPeers();
        if (peers == null || peers.Count == 0) return;

        var peer = peers[Random.Shared.Next(peers.Count)];

        try 
        {
            // 1. Get Roots
            var localRoot = _merkleService.GetRoot();
            var remoteRoot = await _client.GetMerkleRootAsync(peer);

            if (remoteRoot == null) return; // Legacy peer or error

            if (localRoot.Hash == remoteRoot.Hash)
            {
                _logger.LogDebug($"Merkle Tree in sync with {peer.Name}.");
                return;
            }

            _logger.LogInformation($"Hash mismatch with {peer.Name}. Calculating diff...");
            
            // 2. Drill Down
            await SychronizeDifferencesAsync(peer, localRoot, remoteRoot);
        }
        catch(Exception ex)
        {
           _logger.LogError($"Smart Sync failed with {peer.Name}: {ex.Message}");
        }
    }

    private async Task SychronizeDifferencesAsync(PeerInfo peer, MerkleTreeNode local, MerkleTreeNode remote)
    {
        // If hashes equal, nothing to do in this branch
        if (local.Hash == remote.Hash) return;

        // If Leaf (Hour), sync the range
        // Note: Check Level property or Children count. 
        // Our Service defines "Hour" as leaf.
        if (local.Level == "Hour" || remote.Level == "Hour")
        {
             _logger.LogInformation($"Syncing bucket {local.RangeStart} - {local.RangeEnd} from {peer.Name}...");
             // Fetch range
             var logs = await _client.GetHistoryByRangeAsync(peer, local.RangeStart, local.RangeEnd);
             if (logs != null)
             {
                 foreach(var log in logs)
                 {
                     await _syncService.ReceiveRemoteLogAsync(log);
                 }
             }
             return;
        }

        // Else, compare children
        // We need to fetch remote children if we only have the remote parent node
        // Actually, remoteRoot is a just a Node. We need to fetch its children.
        var remoteChildren = await _client.GetMerkleChildrenAsync(peer, remote.Hash);
        var localChildren = _merkleService.GetChildren(local.Hash);

        // Match children by RangeStart
        // Optimization: Iterate over union of ranges
        var allStarts = localChildren.Select(c => c.RangeStart)
                        .Union(remoteChildren.Select(c => c.RangeStart))
                        .Distinct().ToList();

        foreach(var start in allStarts)
        {
            var lChild = localChildren.FirstOrDefault(c => c.RangeStart == start);
            var rChild = remoteChildren.FirstOrDefault(c => c.RangeStart == start);

            if (lChild == null && rChild != null)
            {
                // We miss entire node -> Download everything in that range
                // Or drill down closer? If rChild is Year, we don't want to download explicit whole year?
                // Actually, if we miss it entirely, it means we have NO logs in that period.
                // We should just fetch the range.
                var logs = await _client.GetHistoryByRangeAsync(peer, rChild.RangeStart, rChild.RangeEnd);
                if(logs != null) foreach(var l in logs) await _syncService.ReceiveRemoteLogAsync(l);
            }
            else if (lChild != null && rChild == null)
            {
                // Remote misses our data -> Push? 
                // Currently AntiEntropy is Pull-Based mostly. 
                // We could Push, but let's stick to Pull/Repairing Self. 
                // The remote will pull from us when it runs its AE.
            }
            else if (lChild != null && rChild != null)
            {
                // Both have it, recurse
                await SychronizeDifferencesAsync(peer, lChild, rChild);
            }
        }
    }
}
