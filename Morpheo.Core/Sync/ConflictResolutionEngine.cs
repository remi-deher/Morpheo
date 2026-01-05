using System.Text.Json;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync;

/// <summary>
/// Engine responsible for resolving data conflicts during synchronization.
/// </summary>
public class ConflictResolutionEngine
{
    private readonly IEntityTypeResolver _typeResolver;
    private readonly ILogger<ConflictResolutionEngine> _logger;

    public ConflictResolutionEngine(IEntityTypeResolver typeResolver, ILogger<ConflictResolutionEngine> logger)
    {
        _typeResolver = typeResolver;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a conflict between a local and a remote entity state.
    /// Tries CRDT merge first, falls back to Last-Write-Wins (LWW).
    /// </summary>
    /// <param name="entityName">Name of the entity type.</param>
    /// <param name="localJson">JSON representation of the local state.</param>
    /// <param name="localTs">Timestamp of the local state.</param>
    /// <param name="remoteJson">JSON representation of the remote state.</param>
    /// <param name="remoteTs">Timestamp of the remote state.</param>
    /// <returns>The resolved JSON state.</returns>
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

        // Fallback LWW (Last Write Wins)
        return remoteTs > localTs ? remoteJson : localJson;
    }
}
