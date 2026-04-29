namespace DotMatter.Controller;

/// <summary>
/// Describes why a device operation failed.
/// </summary>
public enum DeviceOperationFailure
{
    /// <summary>The operation did not fail.</summary>
    None = 0,
    /// <summary>The device identifier was not found.</summary>
    NotFound,
    /// <summary>The device is known but not currently connected.</summary>
    NotConnected,
    /// <summary>The operation timed out.</summary>
    Timeout,
    /// <summary>The underlying transport failed.</summary>
    TransportError,
}

/// <summary>Result of a device operation.</summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Failure">The failure reason if unsuccessful.</param>
/// <param name="Error">Optional error message.</param>
public sealed record DeviceOperationResult(
    bool Success,
    DeviceOperationFailure Failure = DeviceOperationFailure.None,
    string? Error = null)
{
    /// <summary>Gets a reusable successful result.</summary>
    public static readonly DeviceOperationResult Succeeded = new(true);
}
