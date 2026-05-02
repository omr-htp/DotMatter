using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>Public event path selector for Matter event reads and subscriptions.</summary>
public sealed class MatterEventPath(ushort? endpointId = null, uint? clusterId = null, uint? eventId = null)
{
    /// <summary>Optional endpoint selector. Null acts as a wildcard.</summary>
    public ushort? EndpointId { get; init; } = endpointId;

    /// <summary>Optional cluster selector. Null acts as a wildcard.</summary>
    public uint? ClusterId { get; init; } = clusterId;

    /// <summary>Optional event selector. Null acts as a wildcard.</summary>
    public uint? EventId { get; init; } = eventId;
}

/// <summary>Raw Matter event metadata and payload captured from an event report.</summary>
public sealed class MatterEventReport(
    ushort endpointId,
    uint clusterId,
    uint eventId,
    ulong eventNumber,
    byte priority,
    ulong? epochTimestamp,
    ulong? systemTimestamp,
    ulong? deltaEpochTimestamp,
    ulong? deltaSystemTimestamp,
    object? data,
    MatterTLV? rawData,
    byte? statusCode = null)
{
    /// <summary>Endpoint that emitted the event when present in the event path.</summary>
    public ushort EndpointId { get; } = endpointId;

    /// <summary>Cluster that emitted the event.</summary>
    public uint ClusterId { get; } = clusterId;

    /// <summary>Event identifier within the cluster.</summary>
    public uint EventId { get; } = eventId;

    /// <summary>Monotonic event number assigned by the device.</summary>
    public ulong EventNumber { get; } = eventNumber;

    /// <summary>Raw Matter event priority value.</summary>
    public byte Priority { get; } = priority;

    /// <summary>Epoch timestamp, when supplied by the device.</summary>
    public ulong? EpochTimestamp { get; } = epochTimestamp;

    /// <summary>System timestamp, when supplied by the device.</summary>
    public ulong? SystemTimestamp { get; } = systemTimestamp;

    /// <summary>Delta epoch timestamp, when supplied by the device.</summary>
    public ulong? DeltaEpochTimestamp { get; } = deltaEpochTimestamp;

    /// <summary>Delta system timestamp, when supplied by the device.</summary>
    public ulong? DeltaSystemTimestamp { get; } = deltaSystemTimestamp;

    /// <summary>Compatibility timestamp view that prefers epoch/system timestamps when present.</summary>
    public ulong Timestamp => EpochTimestamp ?? SystemTimestamp ?? DeltaEpochTimestamp ?? DeltaSystemTimestamp ?? 0;

    /// <summary>Best-effort decoded payload value from the TLV parser.</summary>
    public object? Data { get; } = data;

    /// <summary>Raw TLV payload element, including its context tag, when available.</summary>
    public MatterTLV? RawData { get; } = rawData;

    /// <summary>Status code for status-only event reports.</summary>
    public byte? StatusCode { get; } = statusCode;

    internal static MatterEventReport From(EventReportIB report)
    {
        var data = report.EventData;
        return new MatterEventReport(
            endpointId: data?.EndpointId ?? 0,
            clusterId: data?.ClusterId ?? 0,
            eventId: data?.EventId ?? 0,
            eventNumber: data?.EventNumber ?? 0,
            priority: data?.Priority ?? 0,
            epochTimestamp: data?.EpochTimestamp,
            systemTimestamp: data?.SystemTimestamp,
            deltaEpochTimestamp: data?.DeltaEpochTimestamp,
            deltaSystemTimestamp: data?.DeltaSystemTimestamp,
            data: data?.Data,
            rawData: data?.RawData is { } rawData ? new MatterTLV(rawData.GetBytes()) : null,
            statusCode: report.StatusCode);
    }

    internal static IReadOnlyList<MatterEventReport> FromReports(IReadOnlyList<EventReportIB> reports)
    {
        if (reports.Count == 0)
        {
            return [];
        }

        var mapped = new MatterEventReport[reports.Count];
        for (var i = 0; i < reports.Count; i++)
        {
            mapped[i] = From(reports[i]);
        }

        return mapped;
    }
}

/// <summary>Typed event subscription wrapper over the raw interaction-model subscription.</summary>
public sealed class MatterEventSubscription<TEvent> : IAsyncDisposable
{
    private readonly Subscription _subscription;
    private readonly Func<IReadOnlyList<MatterEventReport>, IReadOnlyList<TEvent>> _mapReports;

    internal MatterEventSubscription(
        Subscription subscription,
        Func<IReadOnlyList<MatterEventReport>, IReadOnlyList<TEvent>> mapReports)
    {
        _subscription = subscription;
        _mapReports = mapReports;

        _subscription.OnEvent += HandleEvents;
        _subscription.OnTerminated += ex => OnTerminated?.Invoke(ex);
    }

    /// <summary>Server-assigned subscription identifier.</summary>
    public uint SubscriptionId => _subscription.SubscriptionId;

    /// <summary>Negotiated minimum reporting interval in seconds.</summary>
    public ushort MinIntervalSeconds => _subscription.MinIntervalSeconds;

    /// <summary>Negotiated maximum reporting interval in seconds.</summary>
    public ushort MaxIntervalSeconds => _subscription.MaxIntervalSeconds;

    /// <summary>Gets a value indicating whether the underlying subscription is active.</summary>
    public bool IsActive => _subscription.IsActive;

    /// <summary>Raised when one or more mapped events arrive.</summary>
    public event Action<IReadOnlyList<TEvent>>? OnEvent;

    /// <summary>Raised when the underlying subscription is terminated.</summary>
    public event Action<Exception?>? OnTerminated;

    private void HandleEvents(IReadOnlyList<EventReportIB> reports)
        => OnEvent?.Invoke(_mapReports(MatterEventReport.FromReports(reports)));

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _subscription.DisposeAsync();
}

/// <summary>Public raw Matter event APIs for direct event reads and subscriptions.</summary>
public static class MatterEvents
{
    /// <summary>Read raw event reports for the requested event paths.</summary>
    public static Task<IReadOnlyList<MatterEventReport>> ReadAsync(
        ISession session,
        IReadOnlyList<MatterEventPath> eventPaths,
        bool fabricFiltered = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(eventPaths);
        if (eventPaths.Count == 0)
        {
            throw new ArgumentException("At least one event path is required.", nameof(eventPaths));
        }

        return InteractionManager.ReadEventsAsync(session, eventPaths.Select(ToInternalPath).ToArray(), fabricFiltered, ct);
    }

    /// <summary>Subscribe to raw event reports for the requested event paths.</summary>
    public static async Task<MatterEventSubscription<MatterEventReport>> SubscribeAsync(
        ISession session,
        IReadOnlyList<MatterEventPath> eventPaths,
        ushort minInterval = 1,
        ushort maxInterval = 60,
        bool keepSubscriptions = true,
        bool fabricFiltered = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(eventPaths);
        if (eventPaths.Count == 0)
        {
            throw new ArgumentException("At least one event path is required.", nameof(eventPaths));
        }

        var subscription = await Subscription.CreateMultiAsync(
            session,
            Array.Empty<AttributePath>(),
            eventPaths.Select(ToInternalPath).ToArray(),
            minInterval,
            maxInterval,
            keepSubscriptions,
            fabricFiltered,
            ct);

        return new MatterEventSubscription<MatterEventReport>(subscription, static reports => reports);
    }

    private static EventPath ToInternalPath(MatterEventPath path)
        => new(path.EndpointId, path.ClusterId, path.EventId);
}
