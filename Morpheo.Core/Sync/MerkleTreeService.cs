using System.Security.Cryptography;
using System.Text;

namespace Morpheo.Core.Sync;

/// <summary>
/// Provides services for calculating Merkle Tree Root Hashes to verify data integrity and consistency across distributed nodes.
/// </summary>
public class MerkleTreeService
{
    /// <summary>
    /// Computes the unique Merkle Root Hash for a given collection of data items.
    /// </summary>
    /// <param name="dataItems">An enumerable collection of data items (typically JSON strings) to be included in the tree.</param>
    /// <returns>The calculated Root Hash as a Base64-encoded string, or an empty string if the collection is empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the <paramref name="dataItems"/> argument is null.</exception>
    /// <remarks>
    /// This method sorts the input data ordinally before processing to ensure determinism. 
    /// Identical sets of data will always produce the same Root Hash regardless of their original order.
    /// </remarks>
    public string ComputeRootHash(IEnumerable<string> dataItems)
    {
        if (dataItems == null) throw new ArgumentNullException(nameof(dataItems));
        
        var leaves = dataItems.OrderBy(x => x, StringComparer.Ordinal).Select(ComputeHash).ToList(); 
        
        if (leaves.Count == 0) return string.Empty;
        
        var currentLevel = leaves;
        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<string>();
            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                var left = currentLevel[i];
                var right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left; 
                nextLevel.Add(ComputeHash(left + right));
            }
            currentLevel = nextLevel;
        }
        
        return currentLevel[0];
    }
    
    /// <summary>
    /// Computes the SHA-256 hash of a specific input string.
    /// </summary>
    /// <param name="input">The string content to hash.</param>
    /// <returns>The resulting hash converted to a Base64 string.</returns>
    private string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
