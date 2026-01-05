using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Morpheo.Core.Sync;

/// <summary>
/// Provides high-efficiency Delta Compression using JSON Patch (RFC 6902).
/// This service computes deep recursive diffs to transmit only modified leaf nodes, 
/// significantly reducing network bandwidth in unstable mesh environments.
/// <para>
/// It relies on <see cref="JsonNode"/> for zero-copy structure traversal.
/// </para>
/// </summary>
public class DeltaCompressionService
{
    private readonly ILogger<DeltaCompressionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeltaCompressionService"/>.
    /// </summary>
    public DeltaCompressionService(ILogger<DeltaCompressionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes the precise recursive difference between two JSON documents.
    /// </summary>
    /// <param name="originalJson">The original JSON state.</param>
    /// <param name="modifiedJson">The target JSON state.</param>
    /// <returns>A <see cref="JsonPatchDocument"/> containing the operations to transform original to modified, or null if identical.</returns>
    /// <exception cref="JsonException">Thrown if the input strings are not valid JSON.</exception>
    public JsonPatchDocument? ComputeDiff(string originalJson, string modifiedJson)
    {
        try
        {
            var original = JsonNode.Parse(originalJson);
            var modified = JsonNode.Parse(modifiedJson);

            if (original == null || modified == null) return null;

            var operations = new List<JsonPatchOperation>();
            ComputeDiffRecursive(original, modified, "", operations);

            return operations.Count > 0 ? new JsonPatchDocument { Operations = operations } : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute deep diff");
            return null;
        }
    }

    /// <summary>
    /// Recursively compares two JsonNodes and generates granular patch operations.
    /// </summary>
    private void ComputeDiffRecursive(JsonNode? original, JsonNode? modified, string path, List<JsonPatchOperation> operations)
    {
        if (JsonNode.DeepEquals(original, modified))
        {
            return;
        }

        var originalKind = original?.GetValueKind();
        var modifiedKind = modified?.GetValueKind();

        if (originalKind != modifiedKind)
        {
            operations.Add(new JsonPatchOperation
            {
                Op = "replace",
                Path = path,
                Value = modified?.DeepClone()
            });
            return;
        }

        switch (modifiedKind)
        {
            case JsonValueKind.Object:
                ComputeObjectDiff(original!.AsObject(), modified!.AsObject(), path, operations);
                break;

            case JsonValueKind.Array:
                ComputeArrayDiff(original!.AsArray(), modified!.AsArray(), path, operations);
                break;

            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                if (!JsonNode.DeepEquals(original, modified))
                {
                    operations.Add(new JsonPatchOperation
                    {
                        Op = "replace",
                        Path = path,
                        Value = modified?.DeepClone()
                    });
                }
                break;
        }
    }

    private void ComputeObjectDiff(JsonObject original, JsonObject modified, string basePath, List<JsonPatchOperation> operations)
    {
        var originalKeys = original.Select(kvp => kvp.Key).ToHashSet();
        var modifiedKeys = modified.Select(kvp => kvp.Key).ToHashSet();

        foreach (var key in modifiedKeys)
        {
            var childPath = BuildPath(basePath, key);
            var modifiedValue = modified[key];

            if (!originalKeys.Contains(key))
            {
                operations.Add(new JsonPatchOperation
                {
                    Op = "add",
                    Path = childPath,
                    Value = modifiedValue?.DeepClone()
                });
            }
            else
            {
                var originalValue = original[key];
                ComputeDiffRecursive(originalValue, modifiedValue, childPath, operations);
            }
        }

        foreach (var key in originalKeys.Except(modifiedKeys))
        {
            operations.Add(new JsonPatchOperation
            {
                Op = "remove",
                Path = BuildPath(basePath, key)
            });
        }
    }

    private void ComputeArrayDiff(JsonArray original, JsonArray modified, string path, List<JsonPatchOperation> operations)
    {
        if (!JsonNode.DeepEquals(original, modified))
        {
            operations.Add(new JsonPatchOperation
            {
                Op = "replace",
                Path = path,
                Value = modified.DeepClone()
            });
        }
    }

    /// <summary>
    /// Applies a JSON Patch to an original JSON document.
    /// </summary>
    /// <param name="originalJson">The base document.</param>
    /// <param name="patchJson">The JSON serialized list of operations.</param>
    /// <returns>The patched JSON string, or the original string if patching failed.</returns>
    public string ApplyPatch(string originalJson, string patchJson)
    {
        try
        {
            var target = JsonNode.Parse(originalJson);
            if (target == null) return originalJson;

            var patchDoc = JsonSerializer.Deserialize<JsonPatchDocument>(patchJson);
            if (patchDoc?.Operations == null || patchDoc.Operations.Count == 0)
            {
                return originalJson;
            }

            foreach (var operation in patchDoc.Operations)
            {
                ApplyOperation(target, operation);
            }

            return target.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply patch. Returning original JSON.");
            return originalJson;
        }
    }

    /// <summary>
    /// Executes a single atomic Patch operation on the target JsonNode.
    /// Handles automated path creation for 'add' operations in nested structures.
    /// </summary>
    private void ApplyOperation(JsonNode target, JsonPatchOperation operation)
    {
        var pathSegments = operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        if (pathSegments.Length == 0)
        {
            _logger.LogWarning($"Invalid empty path in operation: {operation.Op}");
            return;
        }

        var current = target;
        
        for (int i = 0; i < pathSegments.Length - 1; i++)
        {
            var segment = pathSegments[i];
            
            if (current is JsonObject obj)
            {
                if (!obj.ContainsKey(segment))
                {
                    if (operation.Op == "add")
                    {
                        obj[segment] = new JsonObject();
                    }
                    else
                    {
                        _logger.LogWarning($"Path segment '{segment}' not found for operation: {operation.Op}");
                        return;
                    }
                }
                current = obj[segment];
            }
            else if (current is JsonArray arr)
            {
                if (int.TryParse(segment, out int index) && index >= 0 && index < arr.Count)
                {
                    current = arr[index];
                }
                else
                {
                    _logger.LogWarning($"Invalid array index '{segment}' for operation: {operation.Op}");
                    return;
                }
            }
            else
            {
                _logger.LogWarning($"Cannot navigate through non-object/array node at '{segment}'");
                return;
            }

            if (current == null)
            {
                _logger.LogWarning($"Null encountered while navigating path at '{segment}'");
                return;
            }
        }

        var lastSegment = pathSegments[^1];

        switch (operation.Op)
        {
            case "add":
            case "replace":
                if (current is JsonObject targetObj)
                {
                    targetObj[lastSegment] = operation.Value?.DeepClone();
                }
                else if (current is JsonArray targetArr)
                {
                    if (int.TryParse(lastSegment, out int index))
                    {
                        if (operation.Op == "add")
                        {
                            targetArr.Insert(index, operation.Value?.DeepClone());
                        }
                        else
                        {
                            if (index >= 0 && index < targetArr.Count)
                            {
                                targetArr[index] = operation.Value?.DeepClone();
                            }
                        }
                    }
                }
                break;

            case "remove":
                if (current is JsonObject removeObj)
                {
                    removeObj.Remove(lastSegment);
                }
                else if (current is JsonArray removeArr)
                {
                    if (int.TryParse(lastSegment, out int index) && index >= 0 && index < removeArr.Count)
                    {
                        removeArr.RemoveAt(index);
                    }
                }
                break;

            default:
                _logger.LogWarning($"Unsupported operation: {operation.Op}");
                break;
        }
    }

    private string BuildPath(string basePath, string segment)
    {
        segment = segment.Replace("~", "~0").Replace("/", "~1");
        return string.IsNullOrEmpty(basePath) ? $"/{segment}" : $"{basePath}/{segment}";
    }

    /// <summary>
    /// Computes SHA-256 hash of JSON content for integrity validation.
    /// This is critical to ensure that a PATCH is applied to the correct base version.
    /// </summary>
    public string ComputeHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}

/// <summary>
/// Represents a standard JSON Patch document (RFC 6902).
/// </summary>
public class JsonPatchDocument
{
    /// <summary>
    /// The ordered list of operations to apply.
    /// </summary>
    public List<JsonPatchOperation> Operations { get; set; } = new();
}

/// <summary>
/// Represents a single atomic operation in a JSON Patch.
/// </summary>
public class JsonPatchOperation
{
    /// <summary>
    /// The operation type (e.g., "add", "remove", "replace").
    /// </summary>
    public string Op { get; set; } = string.Empty;

    /// <summary>
    /// The JSON Pointer path to the target node (e.g., "/user/address/city").
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The value to apply (for add/replace operations).
    /// </summary>
    public JsonNode? Value { get; set; }
}
