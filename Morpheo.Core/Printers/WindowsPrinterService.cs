using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Printers;

public record LocalPrinter(string Name, string Group);

/// <summary>
/// Interacts with Windows Print Spooler to send RAW jobs (ZPL, ESC/POS, PDF).
/// Uses P/Invoke to winspool.drv for direct printer access bypassing GDI.
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
    /// Enumerates installed Windows printers with exclusion/grouping filters applied.
    /// </summary>
    public Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        var localPrinters = GetAvailablePrintersObjects();
        var names = localPrinters.Select(p => p.Name);

        return Task.FromResult(names);
    }

    /// <summary>
    /// Sends raw bytes directly to Windows printer bypassing GDI.
    /// Uses native winspool.drv API for RAW data type (ZPL, ESC/POS, PCL, etc.).
    /// </summary>
    /// <param name="printerName">Exact printer name as shown in Windows.</param>
    /// <param name="content">Raw byte stream to send to printer.</param>
    /// <exception cref="IOException">Thrown when printer open fails or write error occurs.</exception>
    public Task PrintAsync(string printerName, byte[] content)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("WindowsPrinterService used on non-Windows OS!");
            return Task.CompletedTask;
        }

        try
        {
            RawPrinterHelper.SendBytesToPrinter(printerName, content);
            _logger.LogInformation($"[WINDOWS SPOOLER] Successfully sent {content.Length} bytes to '{printerName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to print to '{printerName}'");
            throw;
        }

        return Task.CompletedTask;
    }

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

    /// <summary>
    /// P/Invoke wrapper for winspool.drv native printer APIs.
    /// Handles RAW data printing bypassing Windows GDI subsystem.
    /// </summary>
    private static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DOCINFOW
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDocName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pOutputFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pDatatype;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinterW(
            [MarshalAs(UnmanagedType.LPWStr)] string pPrinterName,
            out IntPtr phPrinter,
            IntPtr pDefault);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int StartDocPrinterW(
            IntPtr hPrinter,
            int level,
            ref DOCINFOW pDocInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(
            IntPtr hPrinter,
            IntPtr pBytes,
            int dwCount,
            out int dwWritten);

        /// <summary>
        /// Sends raw byte array to Windows printer using native spooler API.
        /// WARNING: Caller must ensure printer name is valid and content is in correct format (ZPL, ESC/POS, etc.).
        /// </summary>
        /// <exception cref="IOException">Thrown when any Win32 printer operation fails.</exception>
        public static void SendBytesToPrinter(string printerName, byte[] bytes)
        {
            IntPtr hPrinter = IntPtr.Zero;
            DOCINFOW docInfo = new DOCINFOW
            {
                pDocName = "Morpheo RAW Print Job",
                pOutputFile = null,
                pDatatype = "RAW"
            };

            try
            {
                if (!OpenPrinterW(printerName, out hPrinter, IntPtr.Zero))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Failed to open printer '{printerName}'. Win32 Error Code: {errorCode} (0x{errorCode:X}). " +
                        $"Ensure printer name is exact and accessible.",
                        errorCode);
                }

                int jobId = StartDocPrinterW(hPrinter, 1, ref docInfo);
                if (jobId == 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Failed to start print document on '{printerName}'. Win32 Error Code: {errorCode} (0x{errorCode:X}). " +
                        $"Printer may be offline or access denied.",
                        errorCode);
                }

                if (!StartPagePrinter(hPrinter))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    EndDocPrinter(hPrinter);
                    throw new IOException(
                        $"Failed to start print page on '{printerName}'. Win32 Error Code: {errorCode} (0x{errorCode:X}).",
                        errorCode);
                }

                IntPtr pUnmanagedBytes = IntPtr.Zero;
                try
                {
                    pUnmanagedBytes = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                    if (!WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out int bytesWritten))
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        throw new IOException(
                            $"Failed to write data to printer '{printerName}'. Win32 Error Code: {errorCode} (0x{errorCode:X}). " +
                            $"Bytes written: {bytesWritten}/{bytes.Length}.",
                            errorCode);
                    }

                    if (bytesWritten != bytes.Length)
                    {
                        throw new IOException(
                            $"Incomplete write to printer '{printerName}'. Expected {bytes.Length} bytes, wrote {bytesWritten} bytes.");
                    }
                }
                finally
                {
                    if (pUnmanagedBytes != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pUnmanagedBytes);
                    }
                }

                if (!EndPagePrinter(hPrinter))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    _logger.LogWarning($"EndPagePrinter failed with error {errorCode}, continuing...");
                }

                if (!EndDocPrinter(hPrinter))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Failed to end print document on '{printerName}'. Win32 Error Code: {errorCode} (0x{errorCode:X}). " +
                        $"Print job may be incomplete.",
                        errorCode);
                }
            }
            finally
            {
                if (hPrinter != IntPtr.Zero)
                {
                    ClosePrinter(hPrinter);
                }
            }
        }

        private static readonly ILogger _logger = 
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }
}
