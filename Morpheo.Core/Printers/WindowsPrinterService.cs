using System.Drawing.Printing;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Printers;

public record LocalPrinter(string Name, string Group);

/// <summary>
/// Windows-specific implementation of the print gateway.
/// Interactions with the Windows Spooler.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsPrinterService : IPrintGateway
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<WindowsPrinterService> _logger;

    public WindowsPrinterService(MorpheoOptions options, ILogger<WindowsPrinterService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Returns the list of available printers, applying exclusions and grouping filters.
    /// </summary>
    /// <returns>A list of printer names.</returns>
    public Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        // Reusing internal logic for groups and exclusions
        var localPrinters = GetAvailablePrintersObjects();
        var names = localPrinters.Select(p => p.Name);

        return Task.FromResult(names);
    }

    /// <summary>
    /// Sends raw binary content (ZPL, PDF, etc.) to the printer.
    /// </summary>
    /// <param name="printerName">The target printer name.</param>
    /// <param name="content">The raw content to print.</param>
    public Task PrintAsync(string printerName, byte[] content)
    {
        // NOTE: On Windows, sending RAW data (like ZPL) bypassing the graphics driver
        // requires using "OpenPrinter", "WritePrinter" (winspool.drv) APIs via P/Invoke.
        // To avoid cluttering this file with P/Invoke definitions, we simulate the action here.

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("WindowsPrinterService used on non-Windows OS!");
            return Task.CompletedTask;
        }

        _logger.LogInformation($"[WINDOWS SPOOLER] Sending {content.Length} bytes to '{printerName}'");

        // TODO (Production): Integrate a 'RawPrinterHelper' class here.
        return Task.CompletedTask;
    }

    // --- Internal Methods (Filtering logic) ---

    public List<LocalPrinter> GetAvailablePrintersObjects()
    {
        var results = new List<LocalPrinter>();

        if (!OperatingSystem.IsWindows())
        {
            return results;
        }

        try
        {
            var installedPrinters = PrinterSettings.InstalledPrinters.Cast<string>().ToList();

            foreach (var printerName in installedPrinters)
            {
                if (IsExcluded(printerName))
                {
                    continue;
                }

                string group = DetermineGroup(printerName);
                results.Add(new LocalPrinter(printerName, group));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Windows printers.");
        }

        return results;
    }

    private bool IsExcluded(string name)
    {
        return _options.Printers.Exclusions.Any(pattern =>
            Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase));
    }

    private string DetermineGroup(string name)
    {
        foreach (var group in _options.Printers.Groups)
        {
            foreach (var pattern in group.Value)
            {
                if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    return group.Key;
            }
        }
        return "DEFAULT";
    }
}
