using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

/// <summary>
/// Core service managing data synchronization, broadcasting, and conflict resolution.
/// </summary>
public class DataSyncService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMorpheoClient _client;
    private readonly INetworkDiscovery _discovery;
    private readonly MorpheoOptions _options;
    private readonly ILogger<DataSyncService> _logger;
    private readonly ConflictResolutionEngine _conflictEngine;
    private readonly ISyncRoutingStrategy _routingStrategy;

    private readonly List<PeerInfo> _peers = new();
    
    /// <summary>
    /// Event raised when a remote change is successfully applied.
    /// </summary>
    public event EventHandler<SyncLog>? DataReceived;

    public DataSyncService(
        IServiceProvider serviceProvider,
        IMorpheoClient client,
        INetworkDiscovery discovery,
        MorpheoOptions options,
        ILogger<DataSyncService> logger,
        ConflictResolutionEngine conflictEngine,
        ISyncRoutingStrategy routingStrategy)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _discovery = discovery;
        _options = options;
        _logger = logger;
        _conflictEngine = conflictEngine;
        _routingStrategy = routingStrategy;

        _discovery.PeerLost += (s, p) => { lock (_peers) { _peers.RemoveAll(x => x.Id == p.Id); } };

        _discovery.PeerFound += (s, peer) =>
        {
            lock (_peers) { if (!_peers.Any(x => x.Id == peer.Id)) _peers.Add(peer); }

            // Wait a bit before starting sync to let the server start
            Task.Run(async () =>
            {
                await Task.Delay(new Random().Next(1000, 3000));
                await SynchronizeWithPeerAsync(peer);
            });
        };
    }

    // --- 1. SEND (SMART ROUTING) ---

    /// <summary>
    /// Broadcasts a local change to the network.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="entity">The entity that changed.</param>
    /// <param name="action">The action performed (UPDATE, DELETE, etc.).</param>
    public async Task BroadcastChangeAsync<T>(T entity, string action) where T : MorpheoEntity
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        var lastLog = await db.SyncLogs
            .Where(l => l.EntityId == entity.Id)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        var vector = lastLog != null ? lastLog.Vector : new VectorClock();
        vector.Increment(_options.NodeName);

        var log = new SyncLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entity.Id,
            EntityName = typeof(T).Name,
            Action = action,
            JsonData = JsonSerializer.Serialize(entity),
            Timestamp = DateTime.UtcNow.Ticks,
            IsFromRemote = false,
            Vector = vector
        };

        db.SyncLogs.Add(log);
        await db.SaveChangesAsync();

        // Launch broadcast via strategy
        _ = Task.Run(() => PushToPeers(log));
    }

    private async Task PushToPeers(SyncLog log)
    {
        // 1. Get potential targets (Discovered peers)
        PeerInfo[] candidates;
        lock (_peers) candidates = _peers.ToArray();

        // 2. Prepare DTO
        var dto = new SyncLogDto(
            log.Id,
            log.EntityId,
            log.EntityName,
            log.JsonData,
            log.Action,
            log.Timestamp,
            log.Vector,
            _options.NodeName
        );

        // 3. Define unitary send action (The "How")
        // This function will be called by the strategy for each chosen recipient
        Func<PeerInfo, SyncLogDto, Task<bool>> sendAction = async (peer, d) =>
        {
            try
            {
                await _client.SendSyncUpdateAsync(peer, d);
                return true; // Success (Implicit HTTP 200 ACK)
            }
            catch
            {
                return false; // Failure
            }
        };

        // 4. Execute Strategy (The "Who" and "In which order")
        // This is where Failover magic happens (Server -> Mesh, etc.)
        await _routingStrategy.PropagateAsync(dto, candidates, sendAction);
    }

    // --- 2. RECEIVE (WITH AGNOSTIC RESOLUTION) ---

    /// <summary>
    /// Receives and processes a log entry from a remote node.
    /// </summary>
    /// <param name="remoteDto">The received log DTO.</param>
    public async Task ReceiveRemoteLogAsync(SyncLogDto remoteDto)
    {
        if (remoteDto == null) return;
        var appliedLog = await ApplyRemoteChangeAsync(remoteDto);

        if (appliedLog != null)
        {
            // RELAY: If we accepted the change, propagate it to other strategies.
            // Beware of loops! VectorClock normally protects us.
            _ = Task.Run(() => PushToPeers(appliedLog));
        }
    }

    private async Task<SyncLog?> ApplyRemoteChangeAsync(SyncLogDto remoteDto)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        if (await db.SyncLogs.AnyAsync(l => l.Id == remoteDto.Id)) return null;

        var localLog = await db.SyncLogs
            .Where(l => l.EntityId == remoteDto.EntityId)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        bool shouldApply = false;
        string finalJsonData = remoteDto.JsonData;

        if (localLog == null)
        {
            shouldApply = true;
        }
        else
        {
            var localVector = localLog.Vector;
            var remoteVector = new VectorClock(remoteDto.VectorClock);
            var relation = localVector.CompareTo(remoteVector);

            switch (relation)
            {
                case VectorRelation.CausedBy:
                    shouldApply = true;
                    break;

                case VectorRelation.Causes:
                    return null;

                case VectorRelation.Concurrent:
                case VectorRelation.Equal:
                    // Use Agnostic Conflict Resolution Engine
                    finalJsonData = _conflictEngine.Resolve(
                        remoteDto.EntityName,
                        localLog.JsonData,
                        localLog.Timestamp,
                        remoteDto.JsonData,
                        remoteDto.Timestamp
                    );

                    if (finalJsonData != localLog.JsonData)
                    {
                        shouldApply = true;
                        _logger.LogInformation($"Conflict resolved (Merge/LWW) for {remoteDto.EntityName}");
                    }
                    break;
            }
        }

        if (shouldApply)
        {
            var newLog = new SyncLog
            {
                Id = remoteDto.Id,
                EntityId = remoteDto.EntityId,
                EntityName = remoteDto.EntityName,
                JsonData = finalJsonData,
                Action = remoteDto.Action,
                Timestamp = remoteDto.Timestamp,
                IsFromRemote = true,
                VectorClockJson = JsonSerializer.Serialize(remoteDto.VectorClock)
            };

            db.SyncLogs.Add(newLog);
            await db.SaveChangesAsync();

            DataReceived?.Invoke(this, newLog);
            return newLog;
        }
        return null;
    }

    // --- 3. COLD SYNC (Catch-up) ---

    private async Task SynchronizeWithPeerAsync(PeerInfo peer)
    {
        try
        {
            long lastTick = 0;
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
                if (await db.SyncLogs.AnyAsync())
                {
                    lastTick = await db.SyncLogs.MaxAsync(l => l.Timestamp);
                }
            }

            // Note: For Cold Sync, we generally pull from any available peer.
            // We could also use a strategy here, but P2P is often enough.
            var batch = await _client.GetHistoryAsync(peer, lastTick);
            if (batch != null && batch.Count > 0)
            {
                foreach (var logDto in batch)
                {
                    await ReceiveRemoteLogAsync(logDto);
                }
                _logger.LogInformation($"COLD SYNC: {batch.Count} elements received from {peer.Name}.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Cold Sync Error with {peer.Name}: {ex.Message}");
        }
    }
}
