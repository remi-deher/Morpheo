using System.Text.Json.Serialization;

namespace Morpheo.Sdk;

/// <summary>
/// Represents the runtime configuration for the application.
/// </summary>
public class RuntimeConfig
{
    /// <summary>
    /// Gets or sets the node name.
    /// Default is "Morpheo-Node".
    /// </summary>
    [JsonPropertyName("nodeName")]
    public string NodeName { get; set; } = "Morpheo-Node";

    /// <summary>
    /// Gets or sets the HTTP port.
    /// Default is 5000.
    /// </summary>
    [JsonPropertyName("httpPort")]
    public int HttpPort { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the database type (e.g., "Sqlite", "Postgres", "SqlServer", "Memory").
    /// Default is "Sqlite".
    /// </summary>
    [JsonPropertyName("databaseType")]
    public string DatabaseType { get; set; } = "Sqlite";

    /// <summary>
    /// Gets or sets the database connection string.
    /// </summary>
    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether mesh networking is enabled.
    /// Default is false.
    /// </summary>
    [JsonPropertyName("enableMesh")]
    public bool EnableMesh { get; set; } = false;

    /// <summary>
    /// Gets or sets the URL of the central server, if applicable.
    /// </summary>
    [JsonPropertyName("centralServerUrl")]
    public string? CentralServerUrl { get; set; }

    /// <summary>
    /// Gets or sets the security configuration.
    /// </summary>
    [JsonPropertyName("security")]
    public SecurityConfig Security { get; set; } = new();
}

/// <summary>
/// Security-specific configuration.
/// </summary>
public class SecurityConfig
{
    /// <summary>
    /// Enrollment mode: "Auto", "Manual", "Secret".
    /// </summary>
    [JsonPropertyName("enrollmentMode")]
    public string EnrollmentMode { get; set; } = "Auto";

    /// <summary>
    /// Shared secret for "Secret" enrollment mode.
    /// </summary>
    [JsonPropertyName("enrollmentSecret")]
    public string EnrollmentSecret { get; set; } = "";
}
