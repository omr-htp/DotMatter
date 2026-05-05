#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace DotMatter.Core;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Base exception for DotMatter.Core failures that should surface to hosting layers.
/// </summary>
public class MatterCoreException : Exception
{
    /// <summary>MatterCoreException.</summary>
    public MatterCoreException(string message)
        : base(message)
    {
    }

    /// <summary>MatterCoreException.</summary>
    public MatterCoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised for TLV encoding and decoding failures.
/// </summary>
public sealed class MatterTlvException : MatterCoreException
{
    /// <summary>MatterTlvException.</summary>
    public MatterTlvException(string message)
        : base(message)
    {
    }

    /// <summary>MatterTlvException.</summary>
    public MatterTlvException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised for transport-level failures such as BLE, UDP, or connection lifecycle errors.
/// </summary>
public class MatterTransportException : MatterCoreException
{
    /// <summary>MatterTransportException.</summary>
    public MatterTransportException(string message)
        : base(message)
    {
    }

    /// <summary>MatterTransportException.</summary>
    public MatterTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when device discovery fails across all supported lookup mechanisms.
/// </summary>
public sealed class MatterDiscoveryException : MatterCoreException
{
    /// <summary>MatterDiscoveryException.</summary>
    public MatterDiscoveryException(string message)
        : base(message)
    {
    }

    /// <summary>MatterDiscoveryException.</summary>
    public MatterDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an operation exceeds its allowed execution time.
/// </summary>
public sealed class MatterTimeoutException : TimeoutException
{
    /// <summary>MatterTimeoutException.</summary>
    public MatterTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>MatterTimeoutException.</summary>
    public MatterTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
