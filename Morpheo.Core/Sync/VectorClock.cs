using System.Text.Json;

namespace Morpheo.Core.Sync;

/// <summary>
/// Relationship between two vector clocks.
/// </summary>
public enum VectorRelation
{
    Equal,      // Identical versions
    CausedBy,   // My vector is older (I need to update)
    Causes,     // My vector is newer (I dominate)
    Concurrent  // CONFLICT : Concurrent event
}

/// <summary>
/// Implementation of a Vector Clock for tracking causal history in a distributed system.
/// </summary>
public class VectorClock : Dictionary<string, long>
{
    public VectorClock() { }

    public VectorClock(IDictionary<string, long> other) : base(other) { }

    /// <summary>
    /// Increments the logical counter for this node (Me).
    /// </summary>
    /// <param name="nodeId">The identifier of this node.</param>
    public void Increment(string nodeId)
    {
        if (!ContainsKey(nodeId))
            this[nodeId] = 0;
        this[nodeId]++;
    }

    /// <summary>
    /// Merges with a received vector (takes the MAX of each entry).
    /// Useful after a reconciliation.
    /// </summary>
    /// <param name="other">The other vector clock to merge with.</param>
    public void Merge(IDictionary<string, long> other)
    {
        foreach (var pair in other)
        {
            if (!ContainsKey(pair.Key))
                this[pair.Key] = pair.Value;
            else
                this[pair.Key] = Math.Max(this[pair.Key], pair.Value);
        }
    }

    /// <summary>
    /// Compares "Me" (this) with an "Other" (other).
    /// Returns the temporal relation between the two.
    /// </summary>
    /// <param name="other">The other vector clock to compare against.</param>
    /// <returns>The <see cref="VectorRelation"/>.</returns>
    public VectorRelation CompareTo(IDictionary<string, long> other)
    {
        bool hasGreater = false;
        bool hasLess = false;

        var allKeys = this.Keys.Union(other.Keys);

        foreach (var key in allKeys)
        {
            long myVal = this.TryGetValue(key, out var v1) ? v1 : 0;
            long otherVal = other.TryGetValue(key, out var v2) ? v2 : 0;

            if (myVal > otherVal) hasGreater = true;
            if (myVal < otherVal) hasLess = true;
        }

        if (!hasGreater && !hasLess) return VectorRelation.Equal;
        if (hasGreater && !hasLess) return VectorRelation.Causes;   // I am the ancestor (I am more complete)
        if (!hasGreater && hasLess) return VectorRelation.CausedBy; // I am the descendant (I am behind)

        return VectorRelation.Concurrent; // Conflict
    }

    // Helpers for Database (Serialization)
    public string ToJson() => JsonSerializer.Serialize(this);
    public static VectorClock FromJson(string json)
        => string.IsNullOrEmpty(json) ? new VectorClock() : JsonSerializer.Deserialize<VectorClock>(json) ?? new VectorClock();
}
