using System.ComponentModel.DataAnnotations;

namespace Morpheo.Core.Data;

/// <summary>
/// Base class for all entities that need to be synchronized between nodes (Android <-> Windows <-> Linux).
/// </summary>
public abstract class MorpheoEntity
{
    // Using GUIDs (String) instead of int (AutoIncrement) because in a distributed system,
    // two nodes creating ID "1" simultaneously would cause a conflict.
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Essential for reconciliation (Simplified time vector)
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    // For "Soft Delete": we never truly delete a row in P2P,
    // we mark it as deleted to propagate the info to others.
    public bool IsDeleted { get; set; } = false;
}
