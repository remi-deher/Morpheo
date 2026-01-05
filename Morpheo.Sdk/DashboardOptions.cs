namespace Morpheo.Sdk;

/// <summary>
/// Configuration options for the Morpheo dashboard.
/// </summary>
public class DashboardOptions
{
    /// <summary>
    /// Gets or sets the URL path for the dashboard.
    /// Default is "/morpheo".
    /// </summary>
    public string Path { get; set; } = "/morpheo";

    /// <summary>
    /// Gets or sets the title of the dashboard.
    /// Default is "Morpheo Node".
    /// </summary>
    public string Title { get; set; } = "Morpheo Node";
}
