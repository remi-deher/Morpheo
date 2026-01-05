using System.Text.Json;
using System.Text.Json.Nodes;

namespace Morpheo.Core.Sync;

/// <summary>
/// Provides differential compression services to optimize data transfer by calculating and applying patches between JSON documents.
/// </summary>
public class DeltaCompressionService
{
    /// <summary>
    /// Generates a patch representing the difference between an original JSON document and a modified version.
    /// </summary>
    /// <param name="originalJson">The original valid JSON string.</param>
    /// <param name="modifiedJson">The modified valid JSON string.</param>
    /// <returns>A JSON string representing the patch needed to transform the original into the modified version.</returns>
    /// <exception cref="ArgumentException">Thrown when either input (original or modified) is null, empty, or not valid JSON.</exception>
    /// <remarks>
    /// This implementation currently uses a "Full Replacement" strategy where the patch is simply the new content, unless the content is identical. 
    /// If the documents are identical, an empty JSON object "{}" is returned.
    /// </remarks>
    public string CreatePatch(string originalJson, string modifiedJson)
    {
        if (string.IsNullOrWhiteSpace(originalJson) || string.IsNullOrWhiteSpace(modifiedJson))
            throw new ArgumentException("JSON inputs cannot be empty");

        try
        {
            var original = JsonNode.Parse(originalJson);
            var modified = JsonNode.Parse(modifiedJson);

            if (JsonNode.DeepEquals(original, modified))
            {
                return "{}"; 
            }

            return modified?.ToJsonString() ?? "null";
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid JSON format", ex);
        }
    }

    /// <summary>
    /// Applies a patch to an original JSON document to produce the modified version.
    /// </summary>
    /// <param name="originalJson">The original valid JSON string.</param>
    /// <param name="patchJson">The patch JSON string to apply.</param>
    /// <returns>The resulting transformed JSON string.</returns>
    /// <exception cref="ArgumentException">Thrown when either input (original or patch) is null, empty, or not valid JSON.</exception>
    /// <remarks>
    /// If the patch is an empty JSON object "{}", the method interprets it as "no change" and returns the original JSON.
    /// Otherwise, it treats the patch as the complete new state (Full Replacement).
    /// </remarks>
    public string ApplyPatch(string originalJson, string patchJson)
    {
        if (string.IsNullOrWhiteSpace(originalJson) || string.IsNullOrWhiteSpace(patchJson))
            throw new ArgumentException("JSON inputs cannot be empty");

        try
        {
            var original = JsonNode.Parse(originalJson);
            var patch = JsonNode.Parse(patchJson);

            if (patch is JsonObject obj && !obj.Any())
            {
                return original?.ToJsonString() ?? "null";
            }

            return patch?.ToJsonString() ?? "null";
        }
        catch (JsonException ex)
        {
             throw new ArgumentException("Invalid JSON format", ex);
        }
    }
}
