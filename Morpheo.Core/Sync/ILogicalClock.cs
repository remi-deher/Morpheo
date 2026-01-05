namespace Morpheo.Core.Sync;

/// <summary>
/// Represents the causal relationship between two distributed events.
/// Used to detect conflicts in non-linear history.
/// </summary>
public enum ClockRelation
{
    /// <summary>
    /// Both clocks represent the exact same state (Vectors are identical).
    /// </summary>
    Equal,

    /// <summary>
    /// The local clock is strictly in the past of the remote clock (Happened-Before).
    /// The local node should apply the remote update.
    /// </summary>
    CausedBy,

    /// <summary>
    /// The local clock dominates the remote clock (Happened-After).
    /// The remote update is obsolete and should be ignored.
    /// </summary>
    Causes,

    /// <summary>
    /// Neither clock dominates the other. Updates happened concurrently on different nodes.
    /// A Conflict Resolution strategy (e.g. LWW, Merge) must be applied.
    /// </summary>
    Concurrent
}

/// <summary>
/// Defines a logical clock mechanism (e.g., Vector Clock, Hybrid Logical Clock) 
/// for tracking causal history in a distributed system.
/// </summary>
public interface ILogicalClock
{
    /// <summary>
    /// Increments the local logical counter to mark a new event on this node.
    /// Should be called before broadcasting any state change.
    /// </summary>
    void Increment();

    /// <summary>
    /// Merges the local clock with a received remote clock state.
    /// Typically takes the maximum of each component (Update = Max(Local, Remote)).
    /// </summary>
    /// <param name="remoteState">The serialized state of the remote clock (e.g. "A:10|B:5").</param>
    void Merge(string? remoteState);

    /// <summary>
    /// Compares the current clock state against a remote clock state to determine causality.
    /// </summary>
    /// <param name="remoteState">The serialized state of the remote clock.</param>
    /// <returns>The temporal relation indicating if the remote event is new, old, or concurrent.</returns>
    ClockRelation CompareTo(string? remoteState);

    /// <summary>
    /// Serializes the current state of the clock for network transport.
    /// </summary>
    /// <returns>A string representation of the clock (format depends on implementation).</returns>
    string Serialize();
}
