namespace Morpheo.Sdk;

/// <summary>
/// Defines the strategy for data propagation within the network.
/// Allows implementing logics such as "Single Server", "Full Mesh", or "Cascade (Failover)".
/// </summary>
public interface ISyncRoutingStrategy
{
    /// <summary>
    /// Executes the propagation of a log entry to appropriate targets.
    /// </summary>
    /// <param name="log">The data to send.</param>
    /// <param name="candidates">The list of currently visible peers (via Discovery).</param>
    /// <param name="sendFunc">
    /// A delegate that performs the actual sending. 
    /// Returns TRUE if the send was successful (ACK), FALSE otherwise.
    /// </param>
    Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc
    );
}
