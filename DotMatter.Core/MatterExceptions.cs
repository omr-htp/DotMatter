namespace DotMatter.Core;

/// <summary>
/// Base exception for all Matter protocol errors.
/// </summary>
public class MatterException : Exception
{
    /// <summary>MatterException.</summary>
    public MatterException(string message) : base(message) { }
    /// <summary>MatterException.</summary>
    public MatterException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A Matter device returned a StatusReport with an error code.
/// </summary>
public class MatterProtocolException(ushort generalCode, uint protocolId, ushort protocolCode) : MatterException($"Matter StatusReport: General={generalCode}, ProtocolId=0x{protocolId:X}, Code={protocolCode}")
{
    /// <summary>GeneralCode.</summary>
    /// <summary>Gets or sets the GeneralCode value.</summary>
    public ushort GeneralCode { get; } = generalCode;
    /// <summary>ProtocolId.</summary>
    /// <summary>Gets or sets the ProtocolId value.</summary>
    public uint ProtocolId { get; } = protocolId;
    /// <summary>ProtocolCode.</summary>
    /// <summary>Gets or sets the ProtocolCode value.</summary>
    public ushort ProtocolCode { get; } = protocolCode;
}

/// <summary>
/// The device reported BUSY status; caller should retry after the indicated delay.
/// </summary>
public class MatterBusyException(ushort generalCode, uint protocolId, ushort protocolCode, TimeSpan retryAfter) : MatterProtocolException(generalCode, protocolId, protocolCode)
{
    /// <summary>RetryAfter.</summary>
    /// <summary>Gets or sets the RetryAfter value.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Session-level failure (CASE rejected, PASE failed, session expired).
/// </summary>
public class MatterSessionException : MatterException
{
    /// <summary>MatterSessionException.</summary>
    public MatterSessionException(string message) : base(message) { }
    /// <summary>MatterSessionException.</summary>
    public MatterSessionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Certificate chain or attestation verification failed.
/// </summary>
public class MatterCertificateException(string message) : MatterException(message)
{
}

/// <summary>
/// Commissioning process failed at a specific step.
/// </summary>
public class MatterCommissioningException : MatterException
{
    /// <summary>Step.</summary>
    /// <summary>Gets or sets the Step value.</summary>
    public string Step
    {
        get;
    }

    /// <summary>MatterCommissioningException.</summary>
    public MatterCommissioningException(string step, string message)
        : base($"Commissioning failed at {step}: {message}")
    {
        Step = step;
    }

    /// <summary>MatterCommissioningException.</summary>
    public MatterCommissioningException(string step, string message, Exception inner)
        : base($"Commissioning failed at {step}: {message}", inner)
    {
        Step = step;
    }
}
