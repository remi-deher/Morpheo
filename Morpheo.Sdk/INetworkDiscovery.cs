namespace Morpheo.Sdk;

/// <summary>
/// Contract for peer discovery via UDP broadcast or multicast.
/// Implementations must handle socket lifecycle and heartbeat timeouts.
/// </summary>
public interface INetworkDiscovery
{
    /// <summary>
    /// Starts broadcasting presence packets asynchronously (non-blocking).
    /// </summary>
    /// <param name="myInfo">Local peer metadata to advertise.</param>
    /// <param name="ct">Cancellation token for graceful shutdown.</param>
    Task StartAdvertisingAsync(PeerInfo myInfo, CancellationToken ct);

    /// <summary>
    /// Starts listening for discovery packets asynchronously (non-blocking).
    /// </summary>
    /// <param name="ct">Cancellation token for graceful shutdown.</param>
    Task StartListeningAsync(CancellationToken ct);

    /// <summary>
    /// Releases UDP sockets and stops discovery threads immediately.
    /// </summary>
    void Stop();

    /// <summary>
    /// Fired when a new peer is discovered (after first Hello packet).
    /// </summary>
    event EventHandler<PeerInfo> PeerFound;

    /// <summary>
    /// Fired when a peer times out (no heartbeat received within threshold).
    /// </summary>
    event EventHandler<PeerInfo> PeerLost;

    /// <summary>
    /// Returns all currently active peers (passed heartbeat checks).
    /// </summary>
    IReadOnlyList<PeerInfo> GetPeers();
}
