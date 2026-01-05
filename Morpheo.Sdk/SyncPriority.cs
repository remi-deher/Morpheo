namespace Morpheo.Sdk;

/// <summary>
/// Defines the priority level of a synchronization message.
/// </summary>
public enum SyncPriority
{
    /// <summary>
    /// Critical updates (e.g., security, immediate config).
    /// </summary>
    Critical = 0,
    
    /// <summary>
    /// High priority business data.
    /// </summary>
    High = 1,
    
    /// <summary>
    /// Standard updates.
    /// </summary>
    Normal = 2,
    
    /// <summary>
    /// Background/Low priority data (e.g., telemetry, large blobs).
    /// </summary>
    Low = 3
}
