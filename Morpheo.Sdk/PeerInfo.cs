using MessagePack;

namespace Morpheo.Sdk;

/// <summary>
/// Represents information about a peer node in the network.
/// </summary>
[MessagePackObject]
public record PeerInfo(
    [property: Key(0)] string Id, 
    [property: Key(1)] string Name, 
    [property: Key(2)] string IpAddress, 
    [property: Key(3)] int Port, 
    [property: Key(4)] NodeRole Role, 
    [property: Key(5)] string[] Tags
);
