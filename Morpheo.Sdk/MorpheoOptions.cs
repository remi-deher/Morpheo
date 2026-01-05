namespace Morpheo.Sdk;

/// <summary>
/// Specifies the role of the node in the network.
/// </summary>
public enum NodeRole
{
    StandardClient,
    Relay,
    Server
}

/// <summary>
/// Configuration options for the Morpheo node.
/// </summary>
public class MorpheoOptions
{
    /// <summary>
    /// Configuration for "White Label" hosting (Windows Service, Linux Daemon, Android Notification).
    /// </summary>
    public HostingOptions Hosting { get; } = new();

    public const int DEFAULT_PORT = 5555;

    /// <summary>
    /// Gets or sets the name of the node.
    /// Default is the machine name.
    /// </summary>
    public string NodeName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Gets or sets the role of the node.
    /// Default is StandardClient.
    /// </summary>
    public NodeRole Role { get; set; } = NodeRole.StandardClient;

    /// <summary>
    /// Gets the printer configuration options.
    /// </summary>
    public PrinterOptions Printers { get; } = new();

    /// <summary>
    /// Gets or sets the list of node capabilities.
    /// </summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets the port used for discovery.
    /// Default is 5555.
    /// </summary>
    public int DiscoveryPort { get; set; } = DEFAULT_PORT;

    /// <summary>
    /// Gets or sets the local storage path.
    /// </summary>
    public string? LocalStoragePath { get; set; }

    /// <summary>
    /// Gets or sets the network discovery interval.
    /// Default is 3 seconds.
    /// </summary>
    public TimeSpan DiscoveryInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets or sets a value indicating whether to use a secure connection.
    /// Default is false.
    /// </summary>
    public bool UseSecureConnection { get; set; } = false;

    /// <summary>
    /// Gets or sets the duration to keep synchronization logs before cleanup.
    /// Default is 30 days.
    /// </summary>
    public TimeSpan LogRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the frequency of the log Garbage Collector execution.
    /// Default is 1 hour.
    /// </summary>
    public TimeSpan CompactionInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the port is invalid.</exception>
    public void Validate()
    {
        if (DiscoveryPort < 1 || DiscoveryPort > 65535)
            throw new ArgumentException("Morpheo port must be between 1 and 65535.");
    }
}
