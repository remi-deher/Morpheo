using System.Text.Json.Serialization;

namespace Morpheo.Sdk;

/// <summary>
/// Represents a node in the Merkle Tree.
/// </summary>
public class MerkleTreeNode
{
    /// <summary>
    /// The computed hash of this node (children or data).
    /// </summary>
    public string Hash { get; set; } = "";

    /// <summary>
    /// Start timestamp (Ticks) of the range covered by this node.
    /// </summary>
    public long RangeStart { get; set; }

    /// <summary>
    /// End timestamp (Ticks) of the range covered by this node.
    /// </summary>
    public long RangeEnd { get; set; }

    /// <summary>
    /// The level/granularity of the node (Root, Year, Month, Day, Hour).
    /// </summary>
    public string Level { get; set; } = "Root"; // Root, Year, Month, Day, Hour

    /// <summary>
    /// Child nodes for navigation.
    /// </summary>
    public List<MerkleTreeNode> Children { get; set; } = new();
}
