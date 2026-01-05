using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Core.Data;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync;

/// <summary>
/// Maintains an in-memory Time-Partitioned Merkle Tree for efficient synchronization.
/// Hierarchy: Root -> Year -> Month -> Day -> Hour (Leaf).
/// </summary>
public class MerkleTreeService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DataSyncService _dataSync;
    private readonly ISyncLogStore _store;
    private readonly ILogger<MerkleTreeService> _logger;
    private readonly object _lock = new();

    // The entire tree root
    private MerkleTreeNode _root;

    public MerkleTreeService(
        IServiceProvider serviceProvider, 
        DataSyncService dataSync, 
        ISyncLogStore store,
        ILogger<MerkleTreeService> logger)
    {
        _serviceProvider = serviceProvider;
        _dataSync = dataSync;
        _store = store;
        _logger = logger;
        _root = new MerkleTreeNode { Level = "Root", RangeStart = 0, RangeEnd = long.MaxValue };
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Merkle Tree from LSM Store...");

        // Load all logs in batches to rebuild tree
        long lastTimestamp = 0;
        int batchSize = 1000;
        List<SyncLog> batch;

        lock (_lock)
        {
            _root.Children.Clear();
        }

        do
        {
            // Note: GetLogsAsync returns logs > sinceTick
            batch = await _store.GetLogsAsync(lastTimestamp, batchSize);
            
            if (batch.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var log in batch)
                    {
                        var contentToHash = $"{log.Id}:{log.Action}:{log.JsonData}";
                        var hash = ComputeSha256(contentToHash);
                        InsertLeaf(log.Timestamp, hash);
                        
                        if (log.Timestamp > lastTimestamp) 
                            lastTimestamp = log.Timestamp;
                    }
                }
            }
        } while (batch.Count == batchSize);

        lock (_lock)
        {
            RecalculateTreeHashes(_root);
        }

        // Subscribe to future updates
        _dataSync.LogAdded += OnDataReceived;
    }

    private void OnDataReceived(object? sender, SyncLog log)
    {
        UpdateItem(log);
    }

    public void UpdateItem(SyncLog log)
    {
        lock (_lock)
        {
            var contentToHash = $"{log.Id}:{log.Action}:{log.JsonData}";
            var hash = ComputeSha256(contentToHash);
            InsertLeaf(log.Timestamp, hash);
            // Updating leaf requires verifying if hash changed?
            // Since SyncLog is append-only usually (updates cause new log?), 
            // wait, we store multiple logs for same entity.
            // But Merkle Tree tracks LOGS, not Entities.
            // So every new log is a new leaf item in the time bucket.
            // We just add it to the bucket XOR sum.
            
            // Re-hash path
            RecalculateTreeHashes(_root); // Ideally optimize to only update path
        }
    }

    // --- Tree Construction ---

    private void InsertLeaf(long timestamp, string itemHash)
    {
        var date = new DateTime(timestamp, DateTimeKind.Utc);
        
        // Level 1: Year
        var yearNode = GetOrAddChild(_root, date.Year.ToString(), "Year", 
            new DateTime(date.Year, 1, 1).Ticks, 
            new DateTime(date.Year, 12, 31, 23, 59, 59).Ticks);

        // Level 2: Month
        var monthNode = GetOrAddChild(yearNode, date.Month.ToString(), "Month",
            new DateTime(date.Year, date.Month, 1).Ticks,
            new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), 23, 59, 59).Ticks);

        // Level 3: Day
        var dayNode = GetOrAddChild(monthNode, date.Day.ToString(), "Day",
            date.Date.Ticks,
            date.Date.AddDays(1).AddTicks(-1).Ticks);

        // Level 4: Hour (Leaf Bucket)
        var hourNode = GetOrAddChild(dayNode, date.Hour.ToString(), "Hour",
            date.Date.AddHours(date.Hour).Ticks,
            date.Date.AddHours(date.Hour + 1).AddTicks(-1).Ticks);

        // Add to Bucket
        // We don't store individual items in the node to save RAM.
        // We combine the hash.
        // XOR is good for order-independent combination (set reconciliation).
        // itemHash is Hex string? Or bytes?
        // Let's work with bytes for XOR.
        
        byte[] currentHashBytes = string.IsNullOrEmpty(hourNode.Hash) ? new byte[32] : Convert.FromHexString(hourNode.Hash);
        byte[] itemHashBytes = Convert.FromHexString(itemHash);
        
        // XOR
        for (int i = 0; i < 32; i++)
        {
            currentHashBytes[i] ^= itemHashBytes[i];
        }
        
        hourNode.Hash = Convert.ToHexString(currentHashBytes);
    }

    private MerkleTreeNode GetOrAddChild(MerkleTreeNode parent, string key, string level, long start, long end)
    {
        // Simple linear search is fine for 12 months / 30 days / 24 hours
        // Key concept: Using RangeStart as ID? Or some ID field?
        // Let's rely on RangeStart match for children identification if implicit.
        // But here we need to find the specific child.
        // We added a "Key" conceptual but not in DTO.
        // Let's search by RangeStart.
        
        var child = parent.Children.FirstOrDefault(c => c.RangeStart == start);
        if (child == null)
        {
            child = new MerkleTreeNode { Level = level, RangeStart = start, RangeEnd = end, Hash = "" };
            parent.Children.Add(child);
        }
        return child;
    }

    private void RecalculateTreeHashes(MerkleTreeNode node)
    {
        if (node.Level == "Hour") return; // Leaf bucket already maintained via XOR

        foreach (var child in node.Children)
        {
            RecalculateTreeHashes(child);
        }

        // Parent Hash = Hash(Combined Children Hashes)
        // Combine by sorting children by RangeStart and concating hashes
        // Note: For parents, we don't XOR, we usually Hash(Concat(ChildHash1, ChildHash2...)).
        
        if (node.Children.Count == 0)
        {
            node.Hash = "";
            return;
        }

        var sortedChildren = node.Children.OrderBy(c => c.RangeStart).ToList();
        var sb = new StringBuilder();
        foreach (var c in sortedChildren)
        {
            sb.Append(c.Hash);
        }
        
        node.Hash = ComputeSha256(sb.ToString());
    }

    // --- Public Queries ---

    public MerkleTreeNode GetRoot()
    {
        lock (_lock) return CloneNode(_root, false);
    }

    public List<MerkleTreeNode> GetChildren(string hash)
    {
        lock (_lock)
        {
            var node = FindNodeByHash(_root, hash);
            return node?.Children.Select(c => CloneNode(c, false)).ToList() ?? new();
        }
    }

    private MerkleTreeNode? FindNodeByHash(MerkleTreeNode current, string hash)
    {
        if (current.Hash == hash) return current;
        foreach (var child in current.Children)
        {
            var result = FindNodeByHash(child, hash);
            if (result != null) return result;
        }
        return null;
    }

    private MerkleTreeNode CloneNode(MerkleTreeNode node, bool includeChildren)
    {
        return new MerkleTreeNode
        {
            Hash = node.Hash,
            Level = node.Level,
            RangeStart = node.RangeStart,
            RangeEnd = node.RangeEnd,
            Children = includeChildren ? node.Children.Select(c => CloneNode(c, true)).ToList() : new()
        };
    }

    private static string ComputeSha256(string rawData)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes);
    }
}
