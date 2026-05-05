using System.Text.Json.Nodes;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.Clusters;

/// <summary>Shared base for generated cluster event wrappers.</summary>
/// <remarks>Initializes a new cluster event wrapper.</remarks>
public abstract class MatterClusterEvent(MatterEventReport report, string clusterName, string eventName)
{
    /// <summary>Gets the raw event report metadata and payload container.</summary>
    public MatterEventReport Report
    {
        get;
    } = report ?? throw new ArgumentNullException(nameof(report));

    /// <summary>Gets the generated cluster name for this wrapper.</summary>
    public string ClusterName
    {
        get;
    } = clusterName ?? throw new ArgumentNullException(nameof(clusterName));

    /// <summary>Gets the generated event name for this wrapper.</summary>
    public string EventName
    {
        get;
    } = eventName ?? throw new ArgumentNullException(nameof(eventName));

    /// <summary>Gets the endpoint identifier that emitted the event.</summary>
    public ushort EndpointId => Report.EndpointId;

    /// <summary>Gets the Matter cluster identifier for the event.</summary>
    public uint ClusterId => Report.ClusterId;

    /// <summary>Gets the Matter event identifier.</summary>
    public uint EventId => Report.EventId;

    /// <summary>Gets the monotonically increasing event number, when provided.</summary>
    public ulong EventNumber => Report.EventNumber;

    /// <summary>Gets the event priority.</summary>
    public byte Priority => Report.Priority;

    /// <summary>Gets the event epoch timestamp, when present.</summary>
    public ulong? EpochTimestamp => Report.EpochTimestamp;

    /// <summary>Gets the event system timestamp, when present.</summary>
    public ulong? SystemTimestamp => Report.SystemTimestamp;

    /// <summary>Gets the delta epoch timestamp, when present.</summary>
    public ulong? DeltaEpochTimestamp => Report.DeltaEpochTimestamp;

    /// <summary>Gets the delta system timestamp, when present.</summary>
    public ulong? DeltaSystemTimestamp => Report.DeltaSystemTimestamp;

    /// <summary>Gets the raw decoded event payload object when available.</summary>
    public object? RawPayload => Report.Data;

    /// <summary>Gets the typed payload object when DotMatter successfully decoded it.</summary>
    public virtual object? TypedPayload => null;

    /// <summary>Gets the decode or recognition reason when DotMatter could not materialize a typed payload.</summary>
    public virtual string? Reason => null;
}

/// <summary>
/// Base class for all generated cluster instances.
/// Provides InvokeCommandAsync / ReadAttributeAsync that delegate to InteractionManager.
/// </summary>
public abstract class ClusterBase(ISession session, ushort endpointId)
{
    /// <summary>Creates a JSON node for a bool value.</summary>
    protected static JsonNode? CreateJsonValue(bool value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for a byte value.</summary>
    protected static JsonNode? CreateJsonValue(byte value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for a signed byte value.</summary>
    protected static JsonNode? CreateJsonValue(sbyte value) => JsonValue.Create((int)value);
    /// <summary>Creates a JSON node for a short value.</summary>
    protected static JsonNode? CreateJsonValue(short value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for an unsigned short value.</summary>
    protected static JsonNode? CreateJsonValue(ushort value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for an int value.</summary>
    protected static JsonNode? CreateJsonValue(int value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for an unsigned int value.</summary>
    protected static JsonNode? CreateJsonValue(uint value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for a long value.</summary>
    protected static JsonNode? CreateJsonValue(long value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for an unsigned long value.</summary>
    protected static JsonNode? CreateJsonValue(ulong value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for a float value.</summary>
    protected static JsonNode? CreateJsonValue(float value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for a double value.</summary>
    protected static JsonNode? CreateJsonValue(double value) => JsonValue.Create(value);
    /// <summary>Creates a JSON node for a string value.</summary>
    protected static JsonNode? CreateJsonValue(string? value) => value is null ? null : JsonValue.Create(value);
    /// <summary>Creates a JSON node for an octet string value.</summary>
    protected static JsonNode? CreateJsonValue(byte[]? value) => value is null ? null : JsonValue.Create(Convert.ToBase64String(value));

    /// <summary>The Matter cluster identifier for this instance.</summary>
    protected abstract uint GetClusterId();

    /// <summary>Endpoint on the target device (default 1 for application endpoints).</summary>
    public ushort EndpointId { get; } = endpointId;

    /// <summary>Active session to the device.</summary>
    protected ISession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Send a cluster command with optional TLV fields.</summary>
    protected Task<InvokeResponse> InvokeCommandAsync(
        uint commandId,
        Action<MatterTLV>? addFields = null,
        CancellationToken ct = default)
        => InteractionManager.ExecCommandAsync(Session, EndpointId, GetClusterId(), commandId, addFields, ct: ct);

    /// <summary>Return a failed task for generated commands that are not fully implemented yet.</summary>
    protected static Task<InvokeResponse> UnsupportedCommandAsync(string detail)
        => Task.FromException<InvokeResponse>(
            new NotSupportedException(
                $"Cluster command is not supported by DotMatter.Core yet: {detail}"));

    /// <summary>Return a failed task for generated attributes that do not have a verified parser yet.</summary>
    protected static Task<T> UnsupportedAttributeAsync<T>(string detail)
        => Task.FromException<T>(
            new NotSupportedException(
                $"Cluster attribute is not supported by DotMatter.Core yet: {detail}"));

    /// <summary>Send a timed cluster command with optional TLV fields.</summary>
    protected Task<InvokeResponse> InvokeTimedCommandAsync(
        uint commandId,
        Action<MatterTLV>? addFields = null,
        ushort timedTimeoutMs = 5000,
        CancellationToken ct = default)
        => InteractionManager.ExecCommandAsync(Session, EndpointId, GetClusterId(), commandId, addFields, true, timedTimeoutMs, ct);

    /// <summary>Read a single attribute value.</summary>
    protected Task<object?> ReadAttributeAsync(uint attributeId, CancellationToken ct = default)
        => InteractionManager.ReadAttributeAsync(Session, EndpointId, GetClusterId(), attributeId, ct);

    /// <summary>Read a typed attribute value (with best-effort cast). For value types, returns default on failure.</summary>
    protected async Task<T> ReadAttributeAsync<T>(uint attributeId, CancellationToken ct = default) where T : struct
    {
        var raw = await ReadAttributeAsync(attributeId, ct);
        return ConvertAttributeValue<T>(raw);
    }

    /// <summary>Read a nullable typed attribute value.</summary>
    protected async Task<T?> ReadNullableAttributeAsync<T>(uint attributeId, CancellationToken ct = default) where T : struct
    {
        var raw = await ReadAttributeAsync(attributeId, ct);
        return raw is null ? null : ConvertAttributeValue<T>(raw);
    }

    /// <summary>Read a reference-type attribute (string, byte[], etc.).</summary>
    protected async Task<T?> ReadRefAttributeAsync<T>(uint attributeId, CancellationToken ct = default) where T : class
    {
        var raw = await ReadAttributeAsync(attributeId, ct);
        if (raw is T typed)
        {
            return typed;
        }

        return null;
    }

    internal static T ConvertAttributeValue<T>(object? raw) where T : struct
    {
        if (raw is T typed)
        {
            return typed;
        }

        if (raw is null)
        {
            return default;
        }

        if (typeof(T).IsEnum)
        {
            try
            {
                var underlying = Enum.GetUnderlyingType(typeof(T));
                var converted = Convert.ChangeType(raw, underlying);
                if (converted != null)
                {
                    return (T)Enum.ToObject(typeof(T), converted);
                }
            }
            catch
            {
                return default;
            }
        }

        try
        {
            return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>Write a single attribute value with an explicit TLV writer.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(
        uint attributeId,
        Action<MatterTLV> writeValue,
        bool timedRequest = false,
        ushort timedTimeoutMs = 5000,
        CancellationToken ct = default)
        => InteractionManager.WriteAttributeAsync(Session, EndpointId, GetClusterId(), attributeId, writeValue, timedRequest, timedTimeoutMs, ct);

    /// <summary>Read a typed array attribute from its raw TLV value.</summary>
    protected async Task<T[]?> ReadArrayAttributeAsync<T>(
        uint attributeId,
        Func<MatterTLV, T> readItem,
        CancellationToken ct = default)
    {
        var raw = await InteractionManager.ReadAttributeTlvAsync(Session, EndpointId, GetClusterId(), attributeId, ct);
        if (raw is null)
        {
            return null;
        }

        var items = new List<T>();
        raw.OpenArray(2);
        while (!raw.IsEndContainerNext())
        {
            items.Add(readItem(raw));
        }

        raw.CloseContainer();
        return [.. items];
    }

    /// <summary>Read a typed struct attribute from its raw TLV value.</summary>
    protected async Task<T?> ReadStructAttributeAsync<T>(
        uint attributeId,
        Func<MatterTLV, T> readValue,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(readValue);

        var raw = await InteractionManager.ReadAttributeTlvAsync(Session, EndpointId, GetClusterId(), attributeId, ct);
        return raw is null ? null : readValue(raw);
    }

    /// <summary>Write a bool attribute.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(uint attributeId, bool value, CancellationToken ct = default)
        => WriteAttributeAsync(attributeId, tlv => tlv.AddBool(2, value), ct: ct);

    /// <summary>Write a byte attribute.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(uint attributeId, byte value, CancellationToken ct = default)
        => WriteAttributeAsync(attributeId, tlv => tlv.AddUInt8(2, value), ct: ct);

    /// <summary>Write a ushort attribute.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(uint attributeId, ushort value, CancellationToken ct = default)
        => WriteAttributeAsync(attributeId, tlv => tlv.AddUInt16(2, value), ct: ct);

    /// <summary>Write a uint attribute.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(uint attributeId, uint value, CancellationToken ct = default)
        => WriteAttributeAsync(attributeId, tlv => tlv.AddUInt32(2, value), ct: ct);

    /// <summary>Write a ulong attribute.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(uint attributeId, ulong value, CancellationToken ct = default)
        => WriteAttributeAsync(attributeId, tlv => tlv.AddUInt64(2, value), ct: ct);

    /// <summary>Write a string attribute.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(uint attributeId, string value, CancellationToken ct = default)
        => WriteAttributeAsync(attributeId, tlv => tlv.AddUTF8String(2, value), ct: ct);

    /// <summary>Write a byte[] attribute.</summary>
    protected Task<WriteResponse> WriteAttributeAsync(uint attributeId, byte[] value, CancellationToken ct = default)
        => WriteAttributeAsync(attributeId, tlv => tlv.AddOctetString(2, value), ct: ct);

    /// <summary>Subscribe to attribute changes on this cluster.</summary>
    protected Task<Subscription> SubscribeAsync(
        uint[]? attributeIds = null,
        uint[]? eventIds = null,
        ushort minInterval = 1,
        ushort maxInterval = 60,
        bool fabricFiltered = false,
        CancellationToken ct = default)
        => Subscription.CreateAsync(Session, EndpointId, GetClusterId(), attributeIds, eventIds, minInterval, maxInterval, fabricFiltered, ct);

    /// <summary>Read raw event reports on this cluster.</summary>
    protected Task<IReadOnlyList<MatterEventReport>> ReadEventReportsAsync(
        uint[]? eventIds = null,
        bool fabricFiltered = false,
        CancellationToken ct = default)
        => InteractionManager.ReadEventsAsync(Session, BuildEventPaths(eventIds), fabricFiltered, ct);

    /// <summary>Read typed event reports on this cluster using a generated mapper.</summary>
    protected async Task<IReadOnlyList<TEvent>> ReadEventsAsync<TEvent>(
        Func<IReadOnlyList<MatterEventReport>, IReadOnlyList<TEvent>> mapReports,
        uint[]? eventIds = null,
        bool fabricFiltered = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mapReports);
        var reports = await ReadEventReportsAsync(eventIds, fabricFiltered, ct);
        return mapReports(reports);
    }

    /// <summary>Subscribe to typed event reports on this cluster using a generated mapper.</summary>
    protected async Task<MatterEventSubscription<TEvent>> SubscribeEventsAsync<TEvent>(
        Func<IReadOnlyList<MatterEventReport>, IReadOnlyList<TEvent>> mapReports,
        uint[]? eventIds = null,
        ushort minInterval = 1,
        ushort maxInterval = 60,
        bool fabricFiltered = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mapReports);
        var subscription = await SubscribeAsync(
            attributeIds: null,
            eventIds: eventIds,
            minInterval: minInterval,
            maxInterval: maxInterval,
            fabricFiltered: fabricFiltered,
            ct: ct);
        return new MatterEventSubscription<TEvent>(subscription, mapReports);
    }

    private EventPath[] BuildEventPaths(uint[]? eventIds)
        => eventIds is { Length: > 0 }
            ? eventIds.Select(eventId => new EventPath(EndpointId, GetClusterId(), eventId)).ToArray()
            : [new EventPath(EndpointId, GetClusterId(), null)];
}
