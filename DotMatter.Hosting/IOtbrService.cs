using Microsoft.Extensions.Logging;

namespace DotMatter.Hosting;

/// <summary>
/// Abstraction over OTBR and <c>ot-ctl</c> interactions so hosting logic is testable.
/// </summary>
public interface IOtbrService
{
    /// <summary>Enables the OTBR SRP server.</summary>
    /// <param name="ct">Token used to cancel the operation.</param>
    Task EnableSrpServerAsync(CancellationToken ct);
    /// <summary>Runs an <c>ot-ctl</c> command and returns its output.</summary>
    /// <param name="command">Command arguments to pass to <c>ot-ctl</c>.</param>
    /// <param name="ct">Token used to cancel the command.</param>
    /// <param name="firstLineOnly">Whether to return only the first output line.</param>
    Task<string?> RunOtCtlAsync(string command, CancellationToken ct, bool firstLineOnly = true);
    /// <summary>Reads the active Thread dataset as a hexadecimal string.</summary>
    /// <param name="ct">Token used to cancel the operation.</param>
    Task<string?> GetActiveDatasetHexAsync(CancellationToken ct);
    /// <summary>Resolves an SRP service name to an address.</summary>
    /// <param name="serviceName">The SRP service name to resolve.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    Task<string?> ResolveSrpServiceAddressAsync(string serviceName, CancellationToken ct);
    /// <summary>Discovers the Thread IP address reported by OTBR.</summary>
    /// <param name="log">Logger used for discovery diagnostics.</param>
    /// <param name="ct">Token used to cancel discovery.</param>
    Task<string?> DiscoverThreadIpAsync(ILogger log, CancellationToken ct);
}
