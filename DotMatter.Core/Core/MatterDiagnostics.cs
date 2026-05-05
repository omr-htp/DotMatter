using System.Diagnostics;
using System.Diagnostics.Metrics;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace DotMatter.Core;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Central diagnostics surface for DotMatter.Core metrics and tracing.
/// </summary>
public static class MatterDiagnostics
{
    /// <summary>ActivitySourceName.</summary>
    /// <summary>The ActivitySourceName value.</summary>
    public const string ActivitySourceName = "DotMatter.Core";
    /// <summary>MeterName.</summary>
    /// <summary>The MeterName value.</summary>
    public const string MeterName = "DotMatter.Core";

    /// <summary>ActivitySource.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    /// <summary>Meter.</summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>SessionConnectAttempts.</summary>
    /// <summary>The SessionConnectAttempts value.</summary>
    public static readonly Counter<long> SessionConnectAttempts =
        Meter.CreateCounter<long>("matter.session.connect.attempts");

    /// <summary>SessionConnectFailures.</summary>
    /// <summary>The SessionConnectFailures value.</summary>
    public static readonly Counter<long> SessionConnectFailures =
        Meter.CreateCounter<long>("matter.session.connect.failures");

    /// <summary>SessionReconnectLoops.</summary>
    /// <summary>The SessionReconnectLoops value.</summary>
    public static readonly Counter<long> SessionReconnectLoops =
        Meter.CreateCounter<long>("matter.session.reconnect.loops");

    /// <summary>DiscoveryAttempts.</summary>
    /// <summary>The DiscoveryAttempts value.</summary>
    public static readonly Counter<long> DiscoveryAttempts =
        Meter.CreateCounter<long>("matter.discovery.attempts");

    /// <summary>DiscoveryFailures.</summary>
    /// <summary>The DiscoveryFailures value.</summary>
    public static readonly Counter<long> DiscoveryFailures =
        Meter.CreateCounter<long>("matter.discovery.failures");

    /// <summary>TlvParseFailures.</summary>
    /// <summary>The TlvParseFailures value.</summary>
    public static readonly Counter<long> TlvParseFailures =
        Meter.CreateCounter<long>("matter.tlv.parse.failures");

    /// <summary>TransportFailures.</summary>
    /// <summary>The TransportFailures value.</summary>
    public static readonly Counter<long> TransportFailures =
        Meter.CreateCounter<long>("matter.transport.failures");
}
