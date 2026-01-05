using System.Text.Json;
using Morpheo.Sdk;

namespace Morpheo.Core.Configuration;

/// <summary>
/// Manages the loading and saving of the runtime configuration.
/// </summary>
public class MorpheoConfigManager
{
    private readonly string _configPath;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public MorpheoConfigManager()
    {
        _configPath = Path.Combine(Directory.GetCurrentDirectory(), "morpheo.runtime.json");
    }

    /// <summary>
    /// Loads the configuration from the JSON file.
    /// </summary>
    /// <returns>The runtime configuration.</returns>
    public RuntimeConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return new RuntimeConfig(); // Default values
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<RuntimeConfig>(json);
            return config ?? new RuntimeConfig();
        }
        catch
        {
            // Fallback in case of read error
            return new RuntimeConfig();
        }
    }

    /// <summary>
    /// Saves the configuration to the JSON file.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    public void Save(RuntimeConfig config)
    {
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(_configPath, json);
    }
}
