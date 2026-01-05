using System.Collections.Concurrent;
using System.Text.Json;

namespace Morpheo.Core.Security;

public class InMemoryPeerTrustStore : IPeerTrustStore
{
    private readonly ConcurrentDictionary<string, string> _trustedPeers = new();

    public bool IsTrusted(string nodeId) => _trustedPeers.ContainsKey(nodeId);

    public Task TrustPeerAsync(string nodeId, string? fingerprint = null)
    {
        _trustedPeers[nodeId] = fingerprint ?? "AUTO";
        return Task.CompletedTask;
    }

    public Task RevokePeerAsync(string nodeId)
    {
        _trustedPeers.TryRemove(nodeId, out _);
        return Task.CompletedTask;
    }
}

public class FilePeerTrustStore : IPeerTrustStore
{
    private readonly string _filePath;
    private Dictionary<string, string> _trustedPeers = new();
    private readonly object _lock = new();

    public FilePeerTrustStore(string filePath = "trusted_peers.json")
    {
        _filePath = filePath;
        Load();
    }

    public bool IsTrusted(string nodeId)
    {
        lock (_lock) return _trustedPeers.ContainsKey(nodeId);
    }

    public Task TrustPeerAsync(string nodeId, string? fingerprint = null)
    {
        lock (_lock)
        {
            _trustedPeers[nodeId] = fingerprint ?? "MANUAL";
            Save();
        }
        return Task.CompletedTask;
    }

    public Task RevokePeerAsync(string nodeId)
    {
        lock (_lock)
        {
            if (_trustedPeers.Remove(nodeId))
            {
                Save();
            }
        }
        return Task.CompletedTask;
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            _trustedPeers = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch { /* Ignore corruption for now */ }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_trustedPeers, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
