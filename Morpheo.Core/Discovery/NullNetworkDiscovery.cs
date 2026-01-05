using Morpheo.Sdk;

namespace Morpheo.Core.Discovery;

/// <summary>
/// No-op implementation of network discovery.
/// Used when discovery is disabled or not supported.
/// </summary>
public class NullNetworkDiscovery : INetworkDiscovery
{
    public event EventHandler<PeerInfo>? PeerFound { add { } remove { } }
    public event EventHandler<PeerInfo>? PeerLost { add { } remove { } }

    public IReadOnlyList<PeerInfo> GetPeers() => Array.Empty<PeerInfo>();

    public Task StartAdvertisingAsync(PeerInfo localPeer, CancellationToken ct) => Task.CompletedTask;
    public Task StartListeningAsync(CancellationToken ct) => Task.CompletedTask;
    public void Stop() { }
}
