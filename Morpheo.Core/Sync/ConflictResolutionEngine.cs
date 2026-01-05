using System.Text.Json;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync;

/// <summary>
/// Providies the core logic for resolving data conflicts between local and remote entity states during synchronization.
/// </summary>
public class ConflictResolutionEngine
{
    private readonly IEntityTypeResolver _typeResolver;
    private readonly ILogger<ConflictResolutionEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictResolutionEngine"/> class.
    /// </summary>
    /// <param name="typeResolver">The resolver used to identify entity types by name.</param>
    /// <param name="logger">The logger used to record errors and information during conflict resolution.</param>
    public ConflictResolutionEngine(IEntityTypeResolver typeResolver, ILogger<ConflictResolutionEngine> logger)
    {
        _typeResolver = typeResolver;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a conflict between a local entity state and a remote entity state.
    /// </summary>
    /// <param name="entityName">The unique name of the entity type being resolved.</param>
    /// <param name="localJson">The JSON string representing the current local state of the entity.</param>
    /// <param name="localTs">The timestamp associated with the local modification.</param>
    /// <param name="remoteJson">The JSON string representing the incoming remote state of the entity.</param>
    /// <param name="remoteTs">The timestamp associated with the remote modification.</param>
    /// <returns>A JSON string representing the resolved state of the entity.</returns>
    /// <remarks>
    /// This method first attempts to resolve the conflict using specific CRDT merge logic (if the entity implements <see cref="IMergeable{T}"/>).
    /// If no such logic exists or if an error occurs, it falls back to a "Last-Write-Wins" (LWW) strategy based on timestamps.
    /// In the event of identical timestamps, a deterministic lexicographical comparison of the JSON content is used as a tie-breaker.
    /// </remarks>
    public string Resolve(string entityName, string localJson, long localTs, string remoteJson, long remoteTs)
    {
        var type = _typeResolver.ResolveType(entityName);

        if (type != null)
        {
            var mergeableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMergeable<>));

            if (mergeableInterface != null)
            {
                try
                {
                    var localObj = JsonSerializer.Deserialize(localJson, type);
                    var remoteObj = JsonSerializer.Deserialize(remoteJson, type);

                    if (localObj != null && remoteObj != null)
                    {
                        var mergeMethod = mergeableInterface.GetMethod("Merge");
                        var resultObj = mergeMethod!.Invoke(localObj, new[] { remoteObj });
                        if (resultObj != null)
                        {
                            return JsonSerializer.Serialize(resultObj, type);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"CRDT merge error for {entityName}");
                }
            }
        }

        if (remoteTs > localTs)
        {
            return remoteJson;
        }
        else if (remoteTs < localTs)
        {
            return localJson;
        }
        else
        {
            return string.Compare(remoteJson, localJson, StringComparison.Ordinal) > 0 ? remoteJson : localJson;
        }
    }
}
