using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Printers;

/// <summary>
/// Linux/macOS print gateway backed by CUPS. RAW byte streams (ZPL, ESC/POS, PCL…)
/// are handed to the spooler via the <c>lp</c> command with the <c>raw</c> filter,
/// which forwards bytes to the printer firmware untouched — the POSIX counterpart of
/// <see cref="WindowsPrinterService"/>'s winspool RAW path.
///
/// Printer enumeration uses <c>lpstat -e</c>. No managed CUPS binding is required,
/// keeping the dependency surface to the standard CUPS CLI present on any cupsd host.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public class CupsPrinterService : IPrintGateway
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<CupsPrinterService> _logger;

    public CupsPrinterService(MorpheoOptions options, ILogger<CupsPrinterService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        // `lpstat -e` lists every configured destination, one name per line.
        var (exit, stdout, stderr) = await RunProcessAsync("lpstat", new[] { "-e" }, stdin: null);
        if (exit != 0)
        {
            _logger.LogError("lpstat failed (exit {Exit}): {Error}", exit, stderr);
            return Enumerable.Empty<string>();
        }

        return stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !IsExcluded(name))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task PrintAsync(string printerName, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new ArgumentNullException(nameof(printerName));
        if (content == null || content.Length == 0)
            throw new ArgumentException("Print content cannot be empty.", nameof(content));

        _logger.LogInformation("Sending {Bytes} bytes to printer '{PrinterName}' via CUPS (lp -o raw)",
            content.Length, printerName);

        // -d <dest>  : target printer
        // -o raw     : bypass CUPS filters, send the stream verbatim to the device
        // -t <title> : job title shown in the queue
        var (exit, _, stderr) = await RunProcessAsync(
            "lp",
            new[] { "-d", printerName, "-o", "raw", "-t", "Morpheo RAW Job" },
            stdin: content);

        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"lp failed for '{printerName}' (exit {exit}): {stderr}. " +
                "Ensure CUPS is running and the destination exists (lpstat -e).");
        }
    }

    // -------------------------------------------------------------------------
    //  Process helper
    // -------------------------------------------------------------------------

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, string[] arguments, byte[]? stdin)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList passes each token to execvp directly — no shell, no quoting,
        // so a printer name cannot inject extra arguments.
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (stdin != null)
        {
            await using var input = process.StandardInput.BaseStream;
            await input.WriteAsync(stdin);
            await input.FlushAsync();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private bool IsExcluded(string name) =>
        _options.Printers.Exclusions.Any(pattern =>
            Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase));
}
