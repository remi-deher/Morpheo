using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;
using Morpheo.Core.Data;
using Morpheo.Core.Diagnostics;

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
    private readonly MorpheoMetrics? _metrics;
    private readonly MerkleTreeService _merkle;

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
        ISyncRoutingStrategy routingStrategy,
        MorpheoMetrics? metrics = null,
        MerkleTreeService? merkle = null)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _discovery = discovery;
        _options = options;
        _logger = logger;
        _conflictEngine = conflictEngine;
        _routingStrategy = routingStrategy;
        _metrics = metrics;
        _merkle = merkle ?? new MerkleTreeService();

        _discovery.PeerLost += (s, p) =>
        {
            lock (_peers) { _peers.RemoveAll(x => x.Id == p.Id); UpdatePeerMetric(); }
        };

        _discovery.PeerFound += (s, peer) =>
        {
            lock (_peers) { if (!_peers.Any(x => x.Id == peer.Id)) { _peers.Add(peer); UpdatePeerMetric(); } }

            // Wait a bit before starting sync to let the server start
            Task.Run(async () =>
            {
                await Task.Delay(Random.Shared.Next(1000, 3000));
                await SynchronizeWithPeerAsync(peer);
            });
        };
    }

    // --- 0. HISTORY (Cold Sync source) ---

    /// <summary>
    /// Default maximum number of logs returned by a single <see cref="GetHistoryAsync"/> page.
    /// Keeps Cold Sync bounded so a node that has been offline for a long time cannot
    /// force the source node to materialize its entire history in one allocation.
    /// </summary>
    public const int DefaultHistoryPageSize = 500;

    /// <summary>
    /// Hard ceiling on the page size a caller may request, to protect the source node
    /// from an over-large <c>limit</c> query parameter.
    /// </summary>
    public const int MaxHistoryPageSize = 2000;

    /// <summary>
    /// Returns a single page of sync logs newer than <paramref name="sinceTick"/> for a
    /// Cold Sync pull, ordered chronologically. The caller paginates by re-issuing the
    /// request with <paramref name="sinceTick"/> set to the <c>Timestamp</c> of the last
    /// item received — the tick itself is the cursor.
    /// </summary>
    /// <param name="sinceTick">Exclusive lower bound on the log timestamp (tick).</param>
    /// <param name="limit">Maximum number of logs to return; clamped to [1, <see cref="MaxHistoryPageSize"/>].</param>
    public async Task<List<SyncLogDto>> GetHistoryAsync(long sinceTick, int limit = DefaultHistoryPageSize)
    {
        if (limit <= 0) limit = DefaultHistoryPageSize;
        if (limit > MaxHistoryPageSize) limit = MaxHistoryPageSize;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        var logs = await db.SyncLogs
            .Where(l => l.Timestamp > sinceTick)
            .OrderBy(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();

        return logs.Select(l => new SyncLogDto(
            l.Id,
            l.EntityId,
            l.EntityName,
            l.JsonData,
            l.Action,
            l.Timestamp,
            l.Vector,
            _options.NodeName
        )).ToList();
    }

    // --- 0b. ANTI-ENTROPY (Merkle reconciliation source) ---
    //
    // Each SyncLog Id is a unique, immutable identifier for one change event, so the
    // *set* of Ids fully characterises which events a node holds. A Merkle root over
    // that set lets two peers detect divergence with a single hash comparison; when
    // they differ, the manifest + by-ids endpoints let the lagging node pull exactly
    // the events it is missing — robust against the clock-skew gaps that a purely
    // tick-based catch-up can silently miss.

    /// <summary>
    /// Computes the Merkle root hash over the set of locally held sync-log Ids.
    /// Two nodes with identical log sets produce identical roots.
    /// </summary>
    public async Task<string> ComputeLocalDigestAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        var ids = await db.SyncLogs.Select(l => l.Id).ToListAsync();
        return _merkle.ComputeRootHash(ids);
    }

    /// <summary>
    /// Returns the full set of locally held sync-log Ids (the reconciliation manifest).
    /// </summary>
    public async Task<List<string>> GetManifestAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        return await db.SyncLogs.Select(l => l.Id).ToListAsync();
    }

    /// <summary>
    /// Returns the sync logs matching the requested Ids, so a peer can pull exactly
    /// the events it has identified as missing.
    /// </summary>
    public async Task<List<SyncLogDto>> GetLogsByIdsAsync(IReadOnlyList<string> ids)
    {
        if (ids == null || ids.Count == 0) return new List<SyncLogDto>();

        // Bound the request so a single call cannot pull an unbounded set.
        var requested = ids.Take(MaxHistoryPageSize).ToHashSet();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        var logs = await db.SyncLogs
            .Where(l => requested.Contains(l.Id))
            .ToListAsync();

        return logs.Select(l => new SyncLogDto(
            l.Id,
            l.EntityId,
            l.EntityName,
            l.JsonData,
            l.Action,
            l.Timestamp,
            l.Vector,
            _options.NodeName
        )).ToList();
    }

    /// <summary>
    /// Returns the subset of <paramref name="candidateIds"/> that are NOT present locally.
    /// Used by a node to discover which of a peer's events it still needs.
    /// </summary>
    public async Task<List<string>> FindMissingIdsAsync(IEnumerable<string> candidateIds)
    {
        var candidates = candidateIds?.ToList() ?? new List<string>();
        if (candidates.Count == 0) return new List<string>();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        // Pull the locally known Ids once and diff in memory — avoids a huge IN clause.
        var localIds = (await db.SyncLogs.Select(l => l.Id).ToListAsync()).ToHashSet();
        return candidates.Where(id => !localIds.Contains(id)).ToList();
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
        using var activity = MorpheoTelemetry.Source.StartActivity("Morpheo.Broadcast");
        activity?.SetTag("morpheo.entity_type", typeof(T).Name);
        activity?.SetTag("morpheo.action", action);

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

        _metrics?.RecordBroadcast();

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

        using var activity = MorpheoTelemetry.Source.StartActivity("Morpheo.Receive");
        activity?.SetTag("morpheo.entity_type", remoteDto.EntityName);
        activity?.SetTag("morpheo.origin_node", remoteDto.OriginNodeId);

        // Protocol-version gate. A payload that predates the field deserializes to 0,
        // which we treat as the original v1 wire format. A value greater than what we
        // implement means the sender speaks a newer, possibly breaking, dialect — we
        // reject it loudly rather than misinterpret its fields.
        var dtoVersion = remoteDto.SchemaVersion == 0 ? 1 : remoteDto.SchemaVersion;
        if (dtoVersion > SyncLogDto.CurrentSchemaVersion)
        {
            _logger.LogWarning(
                "Rejected log {LogId} from {OriginNode}: schema v{RemoteVersion} is newer than supported v{LocalVersion}. Upgrade this node.",
                remoteDto.Id, remoteDto.OriginNodeId, dtoVersion, SyncLogDto.CurrentSchemaVersion);
            return;
        }

        _metrics?.RecordReceived();
        var appliedLog = await ApplyRemoteChangeAsync(remoteDto);

        if (appliedLog != null)
        {
            // RELAY: If we accepted the change, propagate it to other strategies.
            // Beware of loops! VectorClock normally protects us.
            _ = Task.Run(() => PushToPeers(appliedLog));
        }
    }

    /// <summary>
    /// Receives and processes a batch of log entries from a remote node, in order.
    /// Used by the batch push endpoint and bulk catch-up paths.
    /// </summary>
    /// <param name="remoteDtos">The received log DTOs.</param>
    public async Task ReceiveRemoteBatchAsync(IReadOnlyList<SyncLogDto> remoteDtos)
    {
        if (remoteDtos == null) return;

        // Process in timestamp order so causally-earlier changes are applied first,
        // mirroring how a single live stream would have arrived.
        foreach (var dto in remoteDtos.OrderBy(d => d.Timestamp))
        {
            await ReceiveRemoteLogAsync(dto);
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
                        _metrics?.RecordConflictResolved();
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

            try
            {
                db.SyncLogs.Add(newLog);
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                using var checkScope = _serviceProvider.CreateScope();
                var checkDb = checkScope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
                if (await checkDb.SyncLogs.AnyAsync(l => l.Id == remoteDto.Id))
                {
                    _logger.LogDebug("Duplicate log {LogId} insertion prevented by database constraint.", remoteDto.Id);
                    return null;
                }
                throw;
            }

            _metrics?.RecordApplied();
            DataReceived?.Invoke(this, newLog);
            return newLog;
        }
        return null;
    }

    // --- 3. COLD SYNC (Catch-up) ---

    private async Task SynchronizeWithPeerAsync(PeerInfo peer)
    {
        using var activity = MorpheoTelemetry.Source.StartActivity("Morpheo.ColdSync");
        activity?.SetTag("morpheo.peer", peer.Name);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 0. Cheap short-circuit: if our Merkle digests already match, the log sets
            //    are identical and there is nothing to do — one hash comparison instead
            //    of a full history scan.
            if (await DigestsMatchAsync(peer))
            {
                _logger.LogDebug("Digest match with {PeerName}; already in sync.", peer.Name);
                return;
            }

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
            //
            // Pull page by page. The source caps each response at DefaultHistoryPageSize,
            // so a node that missed a huge amount of history catches up incrementally
            // instead of forcing a single unbounded transfer (OOM risk on both ends).
            int totalReceived = 0;
            int pages = 0;
            const int safetyPageCap = 10_000; // Defensive: never loop forever on a misbehaving peer.

            while (pages < safetyPageCap)
            {
                var batch = await _client.GetHistoryAsync(peer, lastTick, DefaultHistoryPageSize);
                if (batch == null || batch.Count == 0)
                    break;

                foreach (var logDto in batch)
                {
                    await ReceiveRemoteLogAsync(logDto);
                    // Advance the cursor by the highest tick seen so the next page
                    // resumes exactly where this one stopped.
                    if (logDto.Timestamp > lastTick) lastTick = logDto.Timestamp;
                }

                totalReceived += batch.Count;
                pages++;

                // A short page means we have drained the peer's backlog.
                if (batch.Count < DefaultHistoryPageSize)
                    break;
            }

            if (totalReceived > 0)
            {
                sw.Stop();
                _metrics?.RecordColdSync(totalReceived, sw.Elapsed.TotalMilliseconds);
                _logger.LogInformation(
                    "COLD SYNC: {Count} elements received from {PeerName} across {Pages} page(s).",
                    totalReceived, peer.Name, pages);
            }

            // Final pass: the tick cursor can miss logs whose timestamp is <= our latest
            // (clock skew between nodes). A Merkle reconciliation closes any residual gap.
            await ReconcileWithPeerAsync(peer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cold Sync Error with {PeerName}", peer.Name);
        }
    }

    /// <summary>
    /// Returns true when this node and the peer hold an identical set of logs,
    /// determined by comparing Merkle root digests. Returns false if the peer's
    /// digest is unavailable (so the caller falls through to a full sync).
    /// </summary>
    private async Task<bool> DigestsMatchAsync(PeerInfo peer)
    {
        try
        {
            var peerDigest = await _client.GetDigestAsync(peer);
            if (string.IsNullOrEmpty(peerDigest)) return false;

            var localDigest = await ComputeLocalDigestAsync();
            return peerDigest == localDigest;
        }
        catch
        {
            // Peer may not implement the digest endpoint (older version) → not a match.
            return false;
        }
    }

    /// <summary>
    /// Merkle anti-entropy reconciliation: if digests differ, pull the peer's manifest,
    /// determine exactly which log Ids we are missing, and fetch only those.
    /// </summary>
    private async Task ReconcileWithPeerAsync(PeerInfo peer)
    {
        using var activity = MorpheoTelemetry.Source.StartActivity("Morpheo.AntiEntropy");
        activity?.SetTag("morpheo.peer", peer.Name);

        try
        {
            if (await DigestsMatchAsync(peer))
                return; // Converged — nothing missing.

            var manifest = await _client.GetManifestAsync(peer);
            if (manifest == null || manifest.Count == 0) return;

            var missing = await FindMissingIdsAsync(manifest);
            if (missing.Count == 0) return; // We may hold extra logs the peer lacks; harmless here.

            int recovered = 0;
            foreach (var chunk in missing.Chunk(DefaultHistoryPageSize))
            {
                var logs = await _client.GetLogsByIdsAsync(peer, chunk);
                if (logs == null || logs.Count == 0) continue;

                await ReceiveRemoteBatchAsync(logs);
                recovered += logs.Count;
            }

            if (recovered > 0)
            {
                _logger.LogInformation(
                    "ANTI-ENTROPY: recovered {Count} missing log(s) from {PeerName} via Merkle reconciliation.",
                    recovered, peer.Name);
            }
        }
        catch (Exception ex)
        {
            // Peer may not support the manifest/by-ids endpoints (older version).
            _logger.LogDebug(ex, "Anti-entropy reconcile skipped for {PeerName}", peer.Name);
        }
    }

    // Must be called while holding the _peers lock.
    private void UpdatePeerMetric() => _metrics?.SetPeerCount(_peers.Count);

    /// <summary>
    /// Readiness probe: returns true when the underlying database is reachable.
    /// Used by the <c>/health/ready</c> endpoint.
    /// </summary>
    public async Task<bool> CanReachDatabaseAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();
            return await db.Database.CanConnectAsync();
        }
        catch
        {
            return false;
        }
    }
}
