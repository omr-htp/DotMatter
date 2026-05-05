namespace DotMatter.Core.InteractionModel;

/// <summary>Matter Interaction Model status codes (spec §8.10).</summary>
public enum MatterStatusCode : byte
{
    /// <summary>Success.</summary>
    Success = 0x00,
    /// <summary>Failure.</summary>
    Failure = 0x01,
    /// <summary>InvalidSubscription.</summary>
    InvalidSubscription = 0x7D,
    /// <summary>UnsupportedAccess.</summary>
    UnsupportedAccess = 0x7E,
    /// <summary>UnsupportedEndpoint.</summary>
    UnsupportedEndpoint = 0x7F,
    /// <summary>InvalidAction.</summary>
    InvalidAction = 0x80,
    /// <summary>UnsupportedCommand.</summary>
    UnsupportedCommand = 0x81,
    /// <summary>InvalidCommand.</summary>
    InvalidCommand = 0x85,
    /// <summary>UnsupportedAttribute.</summary>
    UnsupportedAttribute = 0x86,
    /// <summary>ConstraintError.</summary>
    ConstraintError = 0x87,
    /// <summary>UnsupportedWrite.</summary>
    UnsupportedWrite = 0x88,
    /// <summary>ResourceExhausted.</summary>
    ResourceExhausted = 0x89,
    /// <summary>NotFound.</summary>
    NotFound = 0x8B,
    /// <summary>UnreportableAttribute.</summary>
    UnreportableAttribute = 0x8C,
    /// <summary>InvalidDataType.</summary>
    InvalidDataType = 0x8D,
    /// <summary>UnsupportedRead.</summary>
    UnsupportedRead = 0x8F,
    /// <summary>DataVersionMismatch.</summary>
    DataVersionMismatch = 0x92,
    /// <summary>Timeout.</summary>
    Timeout = 0x94,
    /// <summary>Busy.</summary>
    Busy = 0xC3,
    /// <summary>NeedsTimedInteraction.</summary>
    NeedsTimedInteraction = 0xC6,
    /// <summary>UnsupportedCluster.</summary>
    UnsupportedCluster = 0xC7,
    /// <summary>NoUpstreamSubscription.</summary>
    NoUpstreamSubscription = 0xC5,
    /// <summary>PathsExhausted.</summary>
    PathsExhausted = 0xC8,
    /// <summary>TimedRequestMismatch.</summary>
    TimedRequestMismatch = 0xC9,
    /// <summary>FailsafeRequired.</summary>
    FailsafeRequired = 0xCA,
    /// <summary>InvalidInState.</summary>
    InvalidInState = 0xCB,
    /// <summary>NoCommandResponse.</summary>
    NoCommandResponse = 0xCC,
    /// <summary>WriteIgnored.</summary>
    WriteIgnored = 0xF0,
}

/// <summary>Exception thrown when a Matter device returns a non-success status.</summary>
public class MatterStatusException : Exception
{
    /// <summary>StatusCode.</summary>
    /// <summary>Gets or sets the StatusCode value.</summary>
    public MatterStatusCode StatusCode
    {
        get;
    }
    /// <summary>ClusterStatusCode.</summary>
    /// <summary>Gets or sets the ClusterStatusCode value.</summary>
    public byte? ClusterStatusCode
    {
        get;
    }
    /// <summary>AttributeId.</summary>
    /// <summary>Gets or sets the AttributeId value.</summary>
    public uint? AttributeId
    {
        get;
    }
    /// <summary>CommandId.</summary>
    /// <summary>Gets or sets the CommandId value.</summary>
    public uint? CommandId
    {
        get;
    }

    /// <summary>MatterStatusException.</summary>
    public MatterStatusException(MatterStatusCode statusCode, string? message = null, byte? clusterStatusCode = null)
        : base(message ?? $"Matter status: {statusCode} (0x{(byte)statusCode:X2})")
    {
        StatusCode = statusCode;
        ClusterStatusCode = clusterStatusCode;
    }

    /// <summary>MatterStatusException.</summary>
    public MatterStatusException(MatterStatusCode statusCode, uint attributeId)
        : base($"Matter status: {statusCode} (0x{(byte)statusCode:X2}) for attribute 0x{attributeId:X4}")
    {
        StatusCode = statusCode;
        AttributeId = attributeId;
    }
}

/// <summary>Exception thrown when MRP retransmission fails after all attempts.</summary>
public class MatterRetransmissionException(int attempts) : Exception($"MRP retransmission failed after {attempts} attempts")
{
    /// <summary>AttemptsMade.</summary>
    /// <summary>Gets or sets the AttemptsMade value.</summary>
    public int AttemptsMade { get; } = attempts;
}
