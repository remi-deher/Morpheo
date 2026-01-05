namespace Morpheo.Sdk;

/// <summary>
/// Represents information about a peer node in the network.
/// </summary>
/// <param name="Id">The unique identifier of the peer.</param>
/// <param name="Name">The name of the peer.</param>
/// <param name="IpAddress">The IP address of the peer.</param>
/// <param name="Port">The port number of the peer.</param>
/// <param name="Role">The role of the peer node.</param>
/// <param name="Tags">A list of tags associated with the peer.</param>
public record PeerInfo(string Id, string Name, string IpAddress, int Port, NodeRole Role, string[] Tags);
