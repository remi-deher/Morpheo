using System.Collections.Generic;

namespace Morpheo.Sdk;

/// <summary>
/// Data Transfer Object representing a synchronization log entry.
/// </summary>
/// <param name="Id">The unique identifier of the log.</param>
/// <param name="EntityId">The identifier of the entity associated with the log.</param>
/// <param name="EntityName">The name of the entity.</param>
/// <param name="JsonData">The JSON representation of the entity data.</param>
/// <param name="Action">The action performed (e.g., CREATE, UPDATE, DELETE).</param>
/// <param name="Timestamp">The timestamp of the log creation.</param>
/// <param name="VectorClock">The vector clock for conflict resolution.</param>
/// <param name="OriginNodeId">The identifier of the node where the log originated.</param>
public record SyncLogDto(
    string Id,
    string EntityId,
    string EntityName,
    string JsonData,
    string Action,
    long Timestamp,
    Dictionary<string, long> VectorClock,
    string OriginNodeId
);
