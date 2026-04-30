using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.Clusters;

/// <summary>
/// Base class for all generated cluster instances.
/// Provides InvokeCommandAsync / ReadAttributeAsync that delegate to InteractionManager.
/// </summary>
public abstract class ClusterBase(ISession session, ushort endpointId)
{
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
        CancellationToken ct = default)
        => Subscription.CreateAsync(Session, EndpointId, GetClusterId(), attributeIds, eventIds, minInterval, maxInterval, ct: ct);
}
