using System.Text.Json;
using System.Text.Json.Nodes;

namespace Morpheo.Core.Sync;

/// <summary>
/// Differential compression using RFC 6902 JSON Patch and RFC 6901 JSON Pointer.
///
/// CreatePatch produces a minimal array of add/remove/replace operations that
/// transforms the original document into the modified one field-by-field.
/// Only the changed parts are included — unchanged subtrees produce zero operations.
///
/// ApplyPatch reconstructs the modified document by replaying the operations
/// against the original.
/// </summary>
public class DeltaCompressionService
{
    private readonly record struct PatchOp(string Op, string Path, JsonNode? Value);

    // -------------------------------------------------------------------------
    //  Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Produces a RFC 6902 JSON Patch document (a JSON array of operations)
    /// representing the minimal diff from <paramref name="originalJson"/> to
    /// <paramref name="modifiedJson"/>.
    ///
    /// Returns an empty array <c>[]</c> when the two documents are identical.
    /// </summary>
    public string CreatePatch(string originalJson, string modifiedJson)
    {
        if (string.IsNullOrWhiteSpace(originalJson) || string.IsNullOrWhiteSpace(modifiedJson))
            throw new ArgumentException("JSON inputs cannot be empty");

        JsonNode? original, modified;
        try
        {
            original = JsonNode.Parse(originalJson);
            modified = JsonNode.Parse(modifiedJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid JSON format", ex);
        }

        var array = new JsonArray();

        foreach (var op in BuildDiff(original, modified, ""))
        {
            var opObj = new JsonObject
            {
                ["op"]   = op.Op,
                ["path"] = op.Path
            };

            if (op.Value is not null)
                opObj["value"] = op.Value.DeepClone();

            array.Add(opObj);
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// Applies a RFC 6902 JSON Patch document to <paramref name="originalJson"/>
    /// and returns the resulting JSON string.
    ///
    /// An empty patch array returns the original document unchanged.
    /// </summary>
    public string ApplyPatch(string originalJson, string patchJson)
    {
        if (string.IsNullOrWhiteSpace(originalJson) || string.IsNullOrWhiteSpace(patchJson))
            throw new ArgumentException("JSON inputs cannot be empty");

        JsonNode? doc;
        JsonNode? patch;
        try
        {
            doc   = JsonNode.Parse(originalJson);
            patch = JsonNode.Parse(patchJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid JSON format", ex);
        }

        if (patch is not JsonArray ops)
            throw new ArgumentException("Patch must be a JSON array of RFC 6902 operations.");

        foreach (var opNode in ops)
        {
            if (opNode is not JsonObject opObj) continue;

            var op    = opObj["op"]?.GetValue<string>()    ?? throw new ArgumentException("Missing 'op' field.");
            var path  = opObj["path"]?.GetValue<string>()  ?? throw new ArgumentException("Missing 'path' field.");
            var value = opObj.ContainsKey("value") ? opObj["value"]?.DeepClone() : null;

            doc = ApplyOperation(doc, op, path, value);
        }

        return doc?.ToJsonString() ?? "null";
    }

    // -------------------------------------------------------------------------
    //  Diff — recursive, produces minimal RFC 6902 operations
    // -------------------------------------------------------------------------

    private IEnumerable<PatchOp> BuildDiff(JsonNode? original, JsonNode? modified, string path)
    {
        if (JsonNode.DeepEquals(original, modified))
            yield break;

        // Both objects → diff key by key
        if (original is JsonObject origObj && modified is JsonObject modObj)
        {
            // Keys removed
            foreach (var key in origObj.Select(kv => kv.Key).ToList())
            {
                if (!modObj.ContainsKey(key))
                    yield return new PatchOp("remove", path + "/" + EscapePointer(key), null);
            }

            // Keys added or changed
            foreach (var (key, modValue) in modObj)
            {
                var childPath = path + "/" + EscapePointer(key);

                if (!origObj.ContainsKey(key))
                    yield return new PatchOp("add", childPath, modValue?.DeepClone());
                else
                    foreach (var op in BuildDiff(origObj[key], modValue, childPath))
                        yield return op;
            }
        }
        // Both arrays → index-based comparison
        else if (original is JsonArray origArr && modified is JsonArray modArr)
        {
            int minLen = Math.Min(origArr.Count, modArr.Count);

            // Diff common indices recursively
            for (int i = 0; i < minLen; i++)
                foreach (var op in BuildDiff(origArr[i], modArr[i], path + "/" + i))
                    yield return op;

            // Elements added (RFC 6902 "-" appends to end)
            for (int i = minLen; i < modArr.Count; i++)
                yield return new PatchOp("add", path + "/-", modArr[i]?.DeepClone());

            // Elements removed — iterate from end so earlier indices stay valid
            for (int i = origArr.Count - 1; i >= minLen; i--)
                yield return new PatchOp("remove", path + "/" + i, null);
        }
        // Different types or primitive value changed → replace
        else
        {
            // RFC 6901: empty string "" is the root pointer
            yield return new PatchOp("replace", path == "" ? "" : path, modified?.DeepClone());
        }
    }

    // -------------------------------------------------------------------------
    //  Apply — replays operations on a JsonNode tree
    // -------------------------------------------------------------------------

    private static JsonNode? ApplyOperation(JsonNode? doc, string op, string path, JsonNode? value)
    {
        // Empty path → root replacement
        if (path == "")
            return op is "replace" or "add" ? value?.DeepClone() : doc;

        var segments = ParsePointer(path);
        var parent   = NavigateToParent(doc, segments, out var lastSegment);

        switch (op)
        {
            case "add":
            case "replace":
                if (parent is JsonObject parentObj)
                {
                    parentObj[lastSegment] = value?.DeepClone();
                }
                else if (parent is JsonArray parentArr)
                {
                    if (lastSegment == "-")
                        parentArr.Add(value?.DeepClone());
                    else if (int.TryParse(lastSegment, out int addIdx))
                    {
                        if (op == "add")
                            parentArr.Insert(addIdx, value?.DeepClone());
                        else
                            parentArr[addIdx] = value?.DeepClone();
                    }
                }
                break;

            case "remove":
                if (parent is JsonObject remObj)
                    remObj.Remove(lastSegment);
                else if (parent is JsonArray remArr && int.TryParse(lastSegment, out int remIdx))
                    remArr.RemoveAt(remIdx);
                break;

            default:
                throw new NotSupportedException($"RFC 6902 operation '{op}' is not supported.");
        }

        return doc;
    }

    // -------------------------------------------------------------------------
    //  RFC 6901 JSON Pointer helpers
    // -------------------------------------------------------------------------

    private static string[] ParsePointer(string pointer)
    {
        if (string.IsNullOrEmpty(pointer))
            return Array.Empty<string>();

        return pointer.TrimStart('/')
                      .Split('/')
                      .Select(UnescapePointer)
                      .ToArray();
    }

    private static JsonNode? NavigateToParent(JsonNode? root, string[] segments, out string lastSegment)
    {
        if (segments.Length == 0)
            throw new ArgumentException("Cannot navigate to parent of root pointer.");

        lastSegment  = segments[^1];
        var current  = root;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            current = current switch
            {
                JsonObject obj                                          => obj[segments[i]],
                JsonArray  arr when int.TryParse(segments[i], out int idx) => arr[idx],
                _ => throw new InvalidOperationException(
                         $"Path segment '{segments[i]}' not found while navigating JSON Pointer.")
            };
        }

        return current;
    }

    // RFC 6901 §3: '~' → '~0', '/' → '~1'  (escape order matters)
    private static string EscapePointer(string s)   => s.Replace("~", "~0").Replace("/", "~1");
    private static string UnescapePointer(string s) => s.Replace("~1", "/").Replace("~0", "~");
}
