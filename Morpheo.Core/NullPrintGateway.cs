using Morpheo.Sdk;
using Microsoft.Extensions.Logging;

namespace Morpheo.Core.Printers;

/// <summary>
/// Default implementation of <see cref="IPrintGateway"/> that does nothing.
/// Prevents crashes if no printing strategy is defined (e.g., Linux Server without CUPS).
/// </summary>
public class NullPrintGateway : IPrintGateway
{
    private readonly ILogger<NullPrintGateway> _logger;

    public NullPrintGateway(ILogger<NullPrintGateway> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        return Task.FromResult(Enumerable.Empty<string>());
    }

    /// <inheritdoc/>
    public Task PrintAsync(string printerName, byte[] content)
    {
        _logger.LogWarning($"Simulated Print (NullGateway): Received order for '{printerName}', but no driver is configured.");
        return Task.CompletedTask;
    }
}
