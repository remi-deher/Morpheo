namespace Morpheo.Sdk;

/// <summary>
/// Defines the contract for a print gateway service.
/// </summary>
public interface IPrintGateway
{
    /// <summary>
    /// Retrieves a list of printers installed on the local machine.
    /// </summary>
    /// <returns>A collection of printer names.</returns>
    Task<IEnumerable<string>> GetLocalPrintersAsync();

    /// <summary>
    /// Sends a print job (RAW ZPL, PDF, etc.) to a specified printer.
    /// </summary>
    /// <param name="printerName">The name of the target printer.</param>
    /// <param name="content">The content data to print.</param>
    Task PrintAsync(string printerName, byte[] content);
}
