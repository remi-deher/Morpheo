namespace Morpheo.Core.Security;

/// <summary>
/// Persists and manages the list of trusted peers.
/// </summary>
public interface IPeerTrustStore
{
    /// <summary>
    /// checks if a peer is trusted.
    /// </summary>
    bool IsTrusted(string nodeId);

    /// <summary>
    /// Trusts a peer explicitly.
    /// </summary>
    Task TrustPeerAsync(string nodeId, string? fingerprint = null);

    /// <summary>
    /// Revokes trust for a peer.
    /// </summary>
    Task RevokePeerAsync(string nodeId);
}
