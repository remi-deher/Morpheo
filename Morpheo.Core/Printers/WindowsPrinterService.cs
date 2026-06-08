using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Printers;

public record LocalPrinter(string Name, string Group);

/// <summary>
/// Windows-specific implementation of the print gateway.
/// Sends RAW data streams (ZPL, ESC/POS, PDF passthrough) directly to the Windows Spooler
/// via winspool.drv P/Invoke, bypassing the GDI graphics driver.
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

    // -------------------------------------------------------------------------
    //  P/Invoke declarations — winspool.drv
    // -------------------------------------------------------------------------

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFO pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBuf, int cbBuf, out int pcWritten);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOCINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pOutputFile;

        /// <summary>
        /// The data type tells the spooler how to interpret the stream.
        /// "RAW" = pass bytes directly to the printer firmware (ZPL, ESC/POS, PCL…).
        /// </summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDataType;
    }

    // -------------------------------------------------------------------------
    //  Public API
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        var localPrinters = GetAvailablePrintersObjects();
        return Task.FromResult(localPrinters.Select(p => p.Name));
    }

    /// <inheritdoc/>
    public Task PrintAsync(string printerName, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new ArgumentNullException(nameof(printerName));
        if (content == null || content.Length == 0)
            throw new ArgumentException("Print content cannot be empty.", nameof(content));

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("WindowsPrinterService.PrintAsync called on non-Windows OS — no-op.");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Sending {Bytes} bytes to printer '{PrinterName}' via winspool.drv",
            content.Length, printerName);

        SendRawToPrinter(printerName, content, jobName: "Morpheo RAW Job");

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    //  Core RAW printing logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends a raw byte buffer directly to the Windows Spooler using the "RAW" data type.
    /// The spooler forwards the bytes to the printer firmware without any GDI transformation.
    /// </summary>
    private void SendRawToPrinter(string printerName, byte[] data, string jobName)
    {
        if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"OpenPrinter failed for '{printerName}'. Win32 error: {err}. " +
                "Ensure the printer is installed and the process has print permission.");
        }

        try
        {
            var docInfo = new DOCINFO
            {
                pDocName = jobName,
                pOutputFile = null,
                pDataType = "RAW"   // Critical: tells the spooler not to re-render
            };

            int jobId = StartDocPrinter(hPrinter, 1, ref docInfo);
            if (jobId == 0)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"StartDocPrinter failed. Win32 error: {err}.");
            }

            try
            {
                if (!StartPagePrinter(hPrinter))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"StartPagePrinter failed. Win32 error: {err}.");
                }

                try
                {
                    // Pin the managed byte array so the GC does not move it
                    // while the unmanaged spooler is reading from it.
                    var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try
                    {
                        IntPtr pBytes = handle.AddrOfPinnedObject();
                        if (!WritePrinter(hPrinter, pBytes, data.Length, out int written))
                        {
                            int err = Marshal.GetLastWin32Error();
                            throw new InvalidOperationException(
                                $"WritePrinter failed after {written}/{data.Length} bytes. " +
                                $"Win32 error: {err}.");
                        }

                        _logger.LogDebug("WritePrinter: {Written}/{Total} bytes written to '{PrinterName}'",
                            written, data.Length, printerName);
                    }
                    finally
                    {
                        handle.Free();
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    // -------------------------------------------------------------------------
    //  Printer enumeration helpers (unchanged)
    // -------------------------------------------------------------------------

    public List<LocalPrinter> GetAvailablePrintersObjects()
    {
        var results = new List<LocalPrinter>();

        if (!OperatingSystem.IsWindows())
            return results;

        try
        {
            var installedPrinters = PrinterSettings.InstalledPrinters.Cast<string>().ToList();

            foreach (var printerName in installedPrinters)
            {
                if (IsExcluded(printerName))
                    continue;

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
