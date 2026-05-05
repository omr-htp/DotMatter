using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>Result of an InvokeCommand operation.</summary>
public sealed class InvokeResponse(
    bool success,
    byte statusCode,
    MessageFrame rawFrame,
    MatterTLV? responseFields,
    string? error = null)
{
    /// <summary>True if the device returned InvokeResponse (opcode 0x09).</summary>
    public bool Success { get; } = success;

    /// <summary>Matter status code (0 = SUCCESS).</summary>
    public byte StatusCode { get; } = statusCode;

    /// <summary>The full response frame for advanced inspection.</summary>
    public MessageFrame RawFrame { get; } = rawFrame;

    /// <summary>TLV payload of the response for extracting response fields.</summary>
    public MatterTLV? ResponseFields { get; } = responseFields;

    /// <summary>Failure detail when the invoke returned an unexpected opcode or could not be parsed safely.</summary>
    public string? Error { get; } = error;
}

/// <summary>Result of a WriteAttribute operation.</summary>
public sealed class WriteResponse(bool success, IReadOnlyList<WriteAttributeStatus> attributeStatuses, byte? statusCode = null)
{
    /// <summary>True if all attributes were written successfully (status 0).</summary>
    public bool Success { get; } = success;

    /// <summary>Per-attribute write status codes.</summary>
    public IReadOnlyList<WriteAttributeStatus> AttributeStatuses { get; } = attributeStatuses;

    /// <summary>StatusResponse status code when the write interaction returned a generic status.</summary>
    public byte? StatusCode { get; } = statusCode;
}

/// <summary>Status of a single attribute write.</summary>
public sealed class WriteAttributeStatus(
    uint attributeId,
    byte statusCode,
    ushort? endpointId = null,
    uint? clusterId = null,
    byte? clusterStatusCode = null)
{
    /// <summary>The endpoint that was written, when present in the response path.</summary>
    public ushort? EndpointId { get; } = endpointId;

    /// <summary>The cluster that was written, when present in the response path.</summary>
    public uint? ClusterId { get; } = clusterId;

    /// <summary>The attribute that was written.</summary>
    public uint AttributeId { get; } = attributeId;

    /// <summary>Matter status code (0 = SUCCESS).</summary>
    public byte StatusCode { get; } = statusCode;

    /// <summary>Optional cluster-specific status code.</summary>
    public byte? ClusterStatusCode { get; } = clusterStatusCode;
}

/// <summary>Specifies an attribute path for generic reads and subscriptions. Null fields act as wildcards.</summary>
public sealed class AttributePath(ushort? endpointId = null, uint? clusterId = null, uint? attributeId = null)
{
    /// <summary>Optional endpoint selector. Null acts as a wildcard.</summary>
    public ushort? EndpointId { get; init; } = endpointId;

    /// <summary>Optional cluster selector. Null acts as a wildcard.</summary>
    public uint? ClusterId { get; init; } = clusterId;

    /// <summary>Optional attribute selector. Null acts as a wildcard.</summary>
    public uint? AttributeId { get; init; } = attributeId;
}

/// <summary>Path to a specific event or event wildcard for subscription requests.</summary>
internal sealed class EventPath(ushort? endpointId = null, uint? clusterId = null, uint? eventId = null)
{
    public ushort? EndpointId { get; init; } = endpointId;

    public uint? ClusterId { get; init; } = clusterId;

    public uint? EventId { get; init; } = eventId;
}

/// <summary>A single attribute value returned from a generic read or subscription report.</summary>
public sealed class AttributeReport(ushort endpointId, uint clusterId, uint attributeId, object? data, uint dataVersion)
{
    /// <summary>The endpoint that produced the attribute value.</summary>
    public ushort EndpointId { get; } = endpointId;

    /// <summary>The cluster that produced the attribute value.</summary>
    public uint ClusterId { get; } = clusterId;

    /// <summary>The attribute identifier.</summary>
    public uint AttributeId { get; } = attributeId;

    /// <summary>The decoded attribute payload.</summary>
    public object? Data { get; } = data;

    /// <summary>The data-version associated with the report.</summary>
    public uint DataVersion { get; } = dataVersion;
}
