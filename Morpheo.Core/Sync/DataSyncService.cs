using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

/// <summary>
/// Orchestrates the distributed data synchronization lifecycle.
/// Acting as the central nervous system of the node, it manages:
/// <list type="bullet">
/// <item><description>Outbound broadcast of local changes (push).</description></item>
/// <item><description>Inbound ingestion of remote changes with Conflict Resolution (pull).</description></item>
/// <item><description>Cold synchronization (history catch-up) for new or recovering nodes.</description></item>
/// </list>
/// </summary>
public class DataSyncService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMorpheoClient _client;
    private readonly INetworkDiscovery _discovery;
    private readonly MorpheoOptions _options;
    private readonly ILogger<DataSyncService> _logger;
    private readonly ConflictResolutionEngine _conflictEngine;
    private readonly DeltaCompressionService _compressionService;

    /// <summary>
    /// Raised when a new log entry is successfully persisted to the local store.
    /// Useful for UI reactivity.
    /// </summary>
    public event EventHandler<SyncLog>? LogAdded;

    /// <summary>
    /// Raised when an entity state changes (EntityName, Id, Action).
    /// </summary>
    public event Action<string, string, string>? OnEntityChanged;

    private readonly ISyncLogStore _store;
    private readonly ISyncRoutingStrategy _routingStrategy;
    private readonly ILogicalClock _clock;
    private readonly List<PeerInfo> _peers = new();
    private readonly IEntityTypeResolver _typeResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSyncService"/>.
    /// </summary>
    public DataSyncService(
        IServiceProvider serviceProvider,
        IMorpheoClient client,
        INetworkDiscovery discovery,
        MorpheoOptions options,
        ILogger<DataSyncService> logger,
        ConflictResolutionEngine conflictEngine,
        ISyncRoutingStrategy routingStrategy,
        ILogicalClock clock,
        ISyncLogStore store,
        DeltaCompressionService compressionService,
        IEntityTypeResolver typeResolver)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _discovery = discovery;
        _options = options;
        _logger = logger;
        _conflictEngine = conflictEngine;
        _routingStrategy = routingStrategy;
        _clock = clock;
        _store = store;
        _compressionService = compressionService;
        _typeResolver = typeResolver;

        // Maintain the active peer list for routing
        _discovery.PeerLost += (s, p) => { lock (_peers) { _peers.RemoveAll(x => x.Id == p.Id); } };

        _discovery.PeerFound += (s, peer) =>
        {
            lock (_peers) { if (!_peers.Any(x => x.Id == peer.Id)) _peers.Add(peer); }

            // Trigger Anti-Entropy (Cold Sync) with a random jitter to avoid thundering herd
            Task.Run(async () =>
            {
                await Task.Delay(new Random().Next(1000, 3000));
                await SynchronizeWithPeerAsync(peer);
            });
        };
    }

    /// <summary>
    /// Broadcasts a local mutation to the distributed mesh.
    /// <para>
    /// 1. Increments local logical clock.
    /// 2. Computes a Delta Patch (if applicable) to optimize bandwidth.
    /// 3. Persists to local storage.
    /// 4. Delegates propagation to the configured <see cref="ISyncRoutingStrategy"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of the MorpheoEntity.</typeparam>
    /// <param name="entity">The mutated entity state.</param>
    /// <param name="action">The mutation type (UPDATE, DELETE, etc.).</param>
    /// <param name="priority">The delivery priority (e.g., Critical for Alerts).</param>
    public async Task BroadcastChangeAsync<T>(T entity, string action, SyncPriority priority = SyncPriority.Normal) where T : MorpheoEntity
    {
        _clock.Increment();

        var entityJson = JsonSerializer.Serialize(entity);
        var finalJson = entityJson;
        var finalAction = action;

        // Optimization: Try to convert full UPDATE to granular PATCH
        if (action == "UPDATE")
        {
            var lastLog = await _store.GetLastLogForEntityAsync(entity.Id);
            if (lastLog != null && !lastLog.IsDeleted)
            {
                if (lastLog.Action != "PATCH")
                {
                    var diff = _compressionService.ComputeDiff(lastLog.JsonData, entityJson);
                    if (diff != null)
                    {
                        var patchJson = JsonSerializer.Serialize(diff.Operations);
                        // Only switch to patch if it saves at least 50% bandwidth
                        if (patchJson.Length < entityJson.Length * 0.5)
                        {
                            finalJson = patchJson;
                            finalAction = "PATCH";
                            _logger.LogDebug($"Delta Compression: Reduced size by {100 - (patchJson.Length * 100 / entityJson.Length)}%");
                        }
                    }
                }
            }
        }

        string? baseContentHash = null;
        if (finalAction == "PATCH")
        {
             var lastLog = await _store.GetLastLogForEntityAsync(entity.Id);
             if (lastLog != null)
             {
                 baseContentHash = HashHelper.ComputeHash(lastLog.JsonData);
             }
        }

        var log = new SyncLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entity.Id,
            EntityName = _typeResolver.GetNetworkName(typeof(T)),
            Action = finalAction,
            JsonData = finalJson,
            Timestamp = DateTime.UtcNow.Ticks,
            IsFromRemote = false,
            ClockState = _clock.Serialize(),
            Priority = priority,
            BaseContentHash = baseContentHash
        };

        await _store.AddLogAsync(log);
        
        LogAdded?.Invoke(this, log);

        // Fire-and-forget propagation (don't block the caller)
        _ = Task.Run(() => PushToPeers(log));
    }

    private async Task PushToPeers(SyncLog log)
    {
        PeerInfo[] candidates;
        lock (_peers) candidates = _peers.ToArray();

        var dto = new SyncLogDto(
            log.Id,
            log.EntityId,
            log.EntityName,
            log.JsonData,
            log.Action,
            log.Timestamp,
            log.ClockState,
            _options.NodeName,
            log.Priority,
            log.BaseContentHash
        );

        Func<PeerInfo, SyncLogDto, Task<bool>> sendAction = async (peer, d) =>
        {
            try
            {
                await _client.SendSyncUpdateAsync(peer, d);
                return true;
            }
            catch
            {
                return false;
            }
        };

        await _routingStrategy.PropagateAsync(dto, candidates, sendAction);
    }

    /// <summary>
    /// Ingests a remote synchronization packet and applies it to the local state.
    /// <para>
    /// Uses <see cref="ILogicalClock"/> to detect causality (Happened-Before relation).
    /// If a conflict is detected (Concurrent events), invokes <see cref="ConflictResolutionEngine"/> to merge states.
    /// </para>
    /// </summary>
    /// <param name="remoteDto">The incoming sync payload from a peer.</param>
    public async Task ReceiveRemoteLogAsync(SyncLogDto remoteDto)
    {
        if (remoteDto == null) return;
        var appliedLog = await ApplyRemoteChangeAsync(remoteDto);

        if (appliedLog != null)
        {
            // If the log was accepted (new info), re-gossip it to neighbors
            // ensuring epidemic propagation.
            _ = Task.Run(() => PushToPeers(appliedLog));
        }
    }

    private async Task<SyncLog?> ApplyRemoteChangeAsync(SyncLogDto remoteDto)
    {
        var localLog = await _store.GetLastLogForEntityAsync(remoteDto.EntityId);

        string finalJsonData = remoteDto.JsonData;

        // 1. Handle Patch Application
        if (remoteDto.Action == "PATCH")
        {
            if (localLog == null || localLog.Action == "PATCH" || localLog.IsDeleted)
            {
                _logger.LogWarning($"Received PATCH for {remoteDto.EntityId} but no valid base found. Ignoring.");
                return null;
            }

            if (!string.IsNullOrEmpty(remoteDto.BaseContentHash))
            {
                var localHash = HashHelper.ComputeHash(localLog.JsonData);
                if (localHash != remoteDto.BaseContentHash)
                {
                     _logger.LogError($"PATCH INTEGRITY ERROR: Hash mismatch for {remoteDto.EntityId}. Expected {remoteDto.BaseContentHash}, got {localHash}. Aborting Patch.");
                     return null;
                }
            }

            finalJsonData = _compressionService.ApplyPatch(localLog.JsonData, remoteDto.JsonData);
            _logger.LogDebug($"Applied PATCH to local entity {remoteDto.EntityName}");
        }

        // 2. Causality Check (Vector Clock)
        var relation = _clock.CompareTo(remoteDto.ClockState);
        bool shouldApply = false;

        switch (relation)
        {
            case ClockRelation.CausedBy:
                // Remote is newer. Apply directly.
                shouldApply = true;
                _clock.Merge(remoteDto.ClockState);
                break;

            case ClockRelation.Causes:
                // Local is newer. Ignore remote.
                return null;

            case ClockRelation.Concurrent:
            case ClockRelation.Equal:
                // Conflict detected.
                if (localLog != null)
                {
                    finalJsonData = _conflictEngine.Resolve(
                        remoteDto.EntityName,
                        localLog.JsonData,
                        localLog.Timestamp,
                        finalJsonData,
                        remoteDto.Timestamp
                    );

                    // If resolution resulted in a change, apply it.
                    if (finalJsonData != localLog.JsonData)
                    {
                        shouldApply = true;
                        _logger.LogInformation($"Conflict resolved (Merge/LWW) for {remoteDto.EntityName}");
                        _clock.Merge(remoteDto.ClockState);
                    }
                }
                else
                {
                   // Concurrent but no local state? Treat as new.
                   shouldApply = true;
                   _clock.Merge(remoteDto.ClockState);
                }
                break;
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
                ClockState = remoteDto.ClockState,
                Priority = remoteDto.Priority,
                BaseContentHash = remoteDto.BaseContentHash
            };

            await _store.AddLogAsync(newLog);

            LogAdded?.Invoke(this, newLog);
            OnEntityChanged?.Invoke(newLog.EntityName, newLog.EntityId, newLog.Action);
            return newLog;
        }
        return null;
    }

    /// <summary>
    /// Initiates a Cold Sync (Anti-Entropy) session with a specific peer.
    /// Retrieves missing history updates to heal partitions.
    /// </summary>
    /// <param name="peer">The target peer.</param>
    public virtual async Task SynchronizeWithPeerAsync(PeerInfo peer)
    {
        try
        {
            // TODO: Optimize by sending a Merkle Tree Root or Vector Clock summary
            // instead of asking for everything from 0.
            long lastTick = 0;
            
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

    /// <summary>
    /// Retrieves a batch of sync logs for a given time window.
    /// Used by remote peers during Anti-Entropy checks.
    /// </summary>
    public async Task<List<SyncLogDto>> GetLogsByRangeAsync(long startTick, long endTick)
    {
        var logs = await _store.GetLogsByRangeAsync(startTick, endTick);

        return logs.Select(l => new SyncLogDto(
            l.Id, l.EntityId, l.EntityName, l.JsonData, l.Action, l.Timestamp, l.ClockState, _options.NodeName, l.Priority
        )).ToList();
    }
}
