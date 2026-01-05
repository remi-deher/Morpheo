using MessagePack;

namespace Morpheo.Sdk;

/// <summary>
/// Data Transfer Object representing a synchronization log entry.
/// </summary>
[MessagePackObject]
public record SyncLogDto(
    [property: Key(0)] string Id,
    [property: Key(1)] string EntityId,
    [property: Key(2)] string EntityName,
    [property: Key(3)] string JsonData,
    [property: Key(4)] string Action,
    [property: Key(5)] long Timestamp,
    [property: Key(6)] string ClockState,
    [property: Key(7)] string OriginNodeId,
    [property: Key(8)] SyncPriority Priority = SyncPriority.Normal,
    [property: Key(9)] string? BaseContentHash = null
);
