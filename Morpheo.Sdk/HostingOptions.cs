namespace Morpheo.Sdk;

/// <summary>
/// Configuration options for hosting the service, allowing white-label customization.
/// </summary>
public class HostingOptions
{
    /// <summary>
    /// Gets or sets the public display name of the service or notification.
    /// Default is "Morpheo Node".
    /// </summary>
    public string ServiceDisplayName { get; set; } = "Morpheo Node";

    /// <summary>
    /// Gets or sets the service status message or notification text.
    /// Default is "Le service est actif." (The service is active).
    /// </summary>
    public string ServiceStatusMessage { get; set; } = "Le service est actif.";

    /// <summary>
    /// Gets or sets the name of the icon resource (e.g., for Android).
    /// Default is "ic_stat_morpheo".
    /// </summary>
    public string IconResourceName { get; set; } = "ic_stat_morpheo";

    /// <summary>
    /// Gets or sets a value indicating whether the service should start on OS boot.
    /// Default is true.
    /// </summary>
    public bool StartOnBoot { get; set; } = true;
}
