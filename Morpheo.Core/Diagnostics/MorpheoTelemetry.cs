using System.Diagnostics;
using System.Reflection;

namespace Morpheo.Core.Diagnostics;

/// <summary>
/// Holds the distributed-tracing <see cref="ActivitySource"/> for Morpheo.
///
/// <para>
/// Spans are emitted around the sync operations (broadcast, receive, cold sync,
/// anti-entropy reconciliation). Trace context (<c>traceparent</c>) propagates across
/// nodes automatically: <c>HttpClient</c> injects it on outgoing sync calls and Kestrel
/// extracts it on the receiving node, so a single change can be followed across the mesh.
/// </para>
///
/// <para>
/// To export with OpenTelemetry, the host app subscribes to the source by name:
/// <code>
/// services.AddOpenTelemetry().WithTracing(t => t.AddSource(MorpheoTelemetry.SourceName));
/// </code>
/// </para>
/// </summary>
public static class MorpheoTelemetry
{
    /// <summary>The activity source name to subscribe to from an OpenTelemetry pipeline.</summary>
    public const string SourceName = "Morpheo";

    /// <summary>The shared <see cref="ActivitySource"/> used to start Morpheo spans.</summary>
    public static readonly ActivitySource Source =
        new(SourceName, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0");
}
