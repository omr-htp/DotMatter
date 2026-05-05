using DotMatter.Core.Clusters;
using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>Public generic Matter Interaction Model APIs for controller/client applications.</summary>
public static class MatterInteractions
{
    /// <summary>Read a raw attribute value.</summary>
    public static Task<object?> ReadAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return InteractionManager.ReadAttributeAsync(session, endpointId, clusterId, attributeId, ct);
    }

    /// <summary>Read a raw TLV attribute payload.</summary>
    public static Task<MatterTLV?> ReadAttributeTlvAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return InteractionManager.ReadAttributeTlvAsync(session, endpointId, clusterId, attributeId, ct);
    }

    /// <summary>Read a struct or enum attribute value using the same conversion rules as generated clusters.</summary>
    public static async Task<T> ReadAttributeAsync<T>(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        CancellationToken ct = default)
        where T : struct
        => ClusterBase.ConvertAttributeValue<T>(await ReadAttributeAsync(session, endpointId, clusterId, attributeId, ct));

    /// <summary>Read a nullable struct or enum attribute value.</summary>
    public static async Task<T?> ReadNullableAttributeAsync<T>(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        CancellationToken ct = default)
        where T : struct
    {
        var raw = await ReadAttributeAsync(session, endpointId, clusterId, attributeId, ct);
        return raw is null ? null : ClusterBase.ConvertAttributeValue<T>(raw);
    }

    /// <summary>Read a reference-type attribute value.</summary>
    public static async Task<T?> ReadRefAttributeAsync<T>(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        CancellationToken ct = default)
        where T : class
        => await ReadAttributeAsync(session, endpointId, clusterId, attributeId, ct) as T;

    /// <summary>Read multiple attributes in a single interaction.</summary>
    public static Task<IReadOnlyList<AttributeReport>> ReadAttributesAsync(
        ISession session,
        IReadOnlyList<AttributePath> attributePaths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(attributePaths);
        if (attributePaths.Count == 0)
        {
            throw new ArgumentException("At least one attribute path is required.", nameof(attributePaths));
        }

        return InteractionManager.ReadAttributesAsync(session, attributePaths, ct);
    }

    /// <summary>Write a raw attribute value using a caller-provided TLV writer.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        Action<MatterTLV> writeValue,
        bool timedRequest = false,
        ushort timedTimeoutMs = 5000,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(writeValue);
        return InteractionManager.WriteAttributeAsync(session, endpointId, clusterId, attributeId, writeValue, timedRequest, timedTimeoutMs, ct);
    }

    /// <summary>Write a bool attribute.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        bool value,
        CancellationToken ct = default)
        => WriteAttributeAsync(session, endpointId, clusterId, attributeId, tlv => tlv.AddBool(2, value), ct: ct);

    /// <summary>Write a byte attribute.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        byte value,
        CancellationToken ct = default)
        => WriteAttributeAsync(session, endpointId, clusterId, attributeId, tlv => tlv.AddUInt8(2, value), ct: ct);

    /// <summary>Write a ushort attribute.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        ushort value,
        CancellationToken ct = default)
        => WriteAttributeAsync(session, endpointId, clusterId, attributeId, tlv => tlv.AddUInt16(2, value), ct: ct);

    /// <summary>Write a uint attribute.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        uint value,
        CancellationToken ct = default)
        => WriteAttributeAsync(session, endpointId, clusterId, attributeId, tlv => tlv.AddUInt32(2, value), ct: ct);

    /// <summary>Write a ulong attribute.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        ulong value,
        CancellationToken ct = default)
        => WriteAttributeAsync(session, endpointId, clusterId, attributeId, tlv => tlv.AddUInt64(2, value), ct: ct);

    /// <summary>Write a UTF-8 string attribute.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        string value,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return WriteAttributeAsync(session, endpointId, clusterId, attributeId, tlv => tlv.AddUTF8String(2, value), ct: ct);
    }

    /// <summary>Write an octet-string attribute.</summary>
    public static Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        byte[] value,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return WriteAttributeAsync(session, endpointId, clusterId, attributeId, tlv => tlv.AddOctetString(2, value), ct: ct);
    }

    /// <summary>Invoke a command on any cluster.</summary>
    public static Task<InvokeResponse> InvokeCommandAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint commandId,
        Action<MatterTLV>? addFields = null,
        bool timedRequest = false,
        ushort timedTimeoutMs = 5000,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return InteractionManager.ExecCommandAsync(session, endpointId, clusterId, commandId, addFields, timedRequest, timedTimeoutMs, ct);
    }

    /// <summary>Subscribe to arbitrary attribute and event paths.</summary>
    public static Task<Subscription> SubscribeAsync(
        ISession session,
        IReadOnlyList<AttributePath>? attributePaths = null,
        IReadOnlyList<MatterEventPath>? eventPaths = null,
        ushort minInterval = 1,
        ushort maxInterval = 60,
        bool keepSubscriptions = true,
        bool fabricFiltered = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var effectiveAttributePaths = attributePaths ?? Array.Empty<AttributePath>();
        var effectiveEventPaths = eventPaths?.Select(static path => new EventPath(path.EndpointId, path.ClusterId, path.EventId)).ToArray();

        if (effectiveAttributePaths.Count == 0 && (effectiveEventPaths is null || effectiveEventPaths.Length == 0))
        {
            throw new ArgumentException("At least one attribute or event path is required.");
        }

        return Subscription.CreateMultiAsync(
            session,
            effectiveAttributePaths,
            effectiveEventPaths,
            minInterval,
            maxInterval,
            keepSubscriptions,
            fabricFiltered,
            ct);
    }
}
