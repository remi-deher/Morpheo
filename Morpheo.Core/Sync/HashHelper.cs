using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Morpheo.Core.Sync;

public static class HashHelper
{
    public static string ComputeJsonHash<T>(T entity)
    {
        var json = JsonSerializer.Serialize(entity);
        return ComputeHash(json);
    }
    
    public static string ComputeHash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
