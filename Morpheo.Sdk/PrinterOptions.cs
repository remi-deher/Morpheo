using System.Text.RegularExpressions;

namespace Morpheo.Sdk;

/// <summary>
/// Configuration options for printers.
/// </summary>
public class PrinterOptions
{
    /// <summary>
    /// Gets the list of printer name patterns (Regex) to ignore.
    /// </summary>
    public List<string> Exclusions { get; } = new();

    /// <summary>
    /// Gets the dictionary mapping group names to lists of printer patterns.
    /// </summary>
    public Dictionary<string, List<string>> Groups { get; } = new();

    // -- Fluent API for configuration --

    /// <summary>
    /// Excludes printers whose names match the specified pattern (e.g., "Microsoft.*", "Fax").
    /// </summary>
    /// <param name="pattern">The regex pattern to match printer names.</param>
    /// <returns>The current <see cref="PrinterOptions"/> instance.</returns>
    public PrinterOptions Exclude(string pattern)
    {
        Exclusions.Add(pattern);
        return this; // Allows method chaining
    }

    /// <summary>
    /// Defines a printer group (e.g., "KITCHEN") and associates printers matching the pattern.
    /// </summary>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="pattern">The regex pattern to match printer names.</param>
    /// <returns>The current <see cref="PrinterOptions"/> instance.</returns>
    public PrinterOptions DefineGroup(string groupName, string pattern)
    {
        if (!Groups.ContainsKey(groupName))
            Groups[groupName] = new List<string>();

        Groups[groupName].Add(pattern);
        return this;
    }
}
