using System.Net;
using System.Text.RegularExpressions;

namespace DotMatter.Core.Discovery;

/// <summary>Resolved SRP/DNS endpoint for a commissioned Matter node.</summary>
public sealed record SrpDiscoveryResult(IPAddress Address, ushort Port);

/// <summary>Resolved SRP/DNS endpoint plus the service name that produced it.</summary>
public sealed record SrpDiscoveredService(IPAddress Address, ushort Port, string ServiceName);

/// <summary>
/// Discovers Matter devices on the Thread network via OTBR's SRP server
/// or DNS-SD browse. Falls back to stored IP if discovery fails.
/// </summary>
public partial class SrpDeviceDiscovery
{
    /// <summary>
    /// Discovers the IPv6 address and port of a commissioned Matter device.
    /// Tries SRP server query first, then falls back to stored address.
    /// </summary>
    /// <param name="compressedFabricId">Hex string, e.g. "DD06DB6EFB694001"</param>
    /// <param name="nodeOperationalId">Hex string of the node's operational ID</param>
    /// <param name="fallbackIp">Previously known IP to use if discovery fails</param>
    /// <param name="fallbackPort">Previously known port (default 5540)</param>
    /// <param name="options">Optional discovery options</param>
    /// <returns>Resolved endpoint address and port.</returns>
    public static async Task<SrpDiscoveryResult> DiscoverAsync(
        string compressedFabricId,
        string nodeOperationalId,
        IPAddress? fallbackIp = null,
        ushort fallbackPort = 5540,
        SrpDiscoveryOptions? options = null)
    {
        using var activity = MatterDiagnostics.ActivitySource.StartActivity("matter.discovery");
        MatterDiagnostics.DiscoveryAttempts.Add(1);

        var effectivePort = options?.DefaultPort ?? fallbackPort;

        var result = await TrySrpServerAsync(compressedFabricId, nodeOperationalId);
        if (result is not null)
        {
            return result;
        }

        result = await TryDnsBrowseAsync(compressedFabricId, nodeOperationalId);
        if (result is not null)
        {
            return result;
        }

        if (fallbackIp != null)
        {
            MatterLog.Warn("[Discovery] SRP/DNS failed, using stored IP {Ip}.", fallbackIp);
            return new SrpDiscoveryResult(fallbackIp, effectivePort);
        }

        MatterDiagnostics.DiscoveryFailures.Add(1);
        throw new MatterDiscoveryException("Device not found via SRP or DNS-SD, and no fallback IP available.");
    }

    /// <summary>
    /// Discovers any Matter device on this fabric via SRP server.
    /// Returns the first device found.
    /// </summary>
    public static async Task<SrpDiscoveredService?> DiscoverAnyAsync(
        string compressedFabricId)
    {
        try
        {
            var output = await RunOtCtlAsync("srp server service");
            if (string.IsNullOrWhiteSpace(output) || output.Trim() == "Done")
            {
                return null;
            }

            // Parse SRP output for any service matching our fabric
            var pattern = $@"({Regex.Escape(compressedFabricId)}-[0-9A-Fa-f]+)\._matter\._tcp";
            var match = Regex.Match(output, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var serviceName = match.Groups[1].Value;
            var addrPort = ParseSrpEntry(output, serviceName);
            if (addrPort is not null)
            {
                return new SrpDiscoveredService(addrPort.Address, addrPort.Port, serviceName);
            }
        }
        catch (Exception ex)
        {
            MatterLog.Warn(ex, "[Discovery] SRP browse failed.");
        }

        return null;
    }

    private static async Task<SrpDiscoveryResult?> TrySrpServerAsync(
        string compressedFabricId, string nodeOperationalId)
    {
        try
        {
            var output = await RunOtCtlAsync("srp server service");
            if (string.IsNullOrWhiteSpace(output) || output.Trim() == "Done")
            {
                return null;
            }

            // Service name format: {CompressedFabricId}-{NodeOperationalId}._matter._tcp
            var serviceName = $"{compressedFabricId}-{nodeOperationalId}";
            return ParseSrpEntry(output, serviceName);
        }
        catch (Exception ex)
        {
            MatterLog.Warn(ex, "[Discovery] SRP query failed.");
            return null;
        }
    }

    private static SrpDiscoveryResult? ParseSrpEntry(string output, string serviceName)
    {
        // Find the block for this service
        var lines = output.Split('\n');
        bool inBlock = false;
        IPAddress? foundIp = null;
        ushort foundPort = 5540;

        foreach (var line in lines)
        {
            if (line.Contains(serviceName, StringComparison.OrdinalIgnoreCase))
            {
                inBlock = true;
                continue;
            }

            if (inBlock)
            {
                // Empty line, "Done", or new service entry ends this block
                if (string.IsNullOrWhiteSpace(line) || line.Trim() == "Done")
                {
                    break;
                }
                // Non-indented line = start of next service entry
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.Contains("._matter._tcp"))
                {
                    break;
                }

                var trimmed = line.Trim();

                // Parse port
                if (trimmed.StartsWith("port:"))
                {
                    var portStr = trimmed.Replace("port:", "").Trim();
                    _ = ushort.TryParse(portStr, out foundPort);
                }

                // Parse host addresses - look for IPv6 addresses (strip zone IDs like %eth0)
                var ipv6Match = Ipv6AddressPattern().Match(trimmed);
                if (ipv6Match.Success)
                {
                    var candidate = ipv6Match.Groups[1].Value;
                    // Prefer mesh-local or SLAAC addresses (fd-prefix), skip link-local (fe80)
                    if (IPAddress.TryParse(candidate, out var parsed))
                    {
                        if (!candidate.StartsWith("fe80", StringComparison.OrdinalIgnoreCase))
                        {
                            foundIp = parsed;
                        }
                        else
                        {
                            foundIp ??= parsed;
                        }
                    }
                }
            }
        }

        if (foundIp != null)
        {
            MatterLog.Info("[Discovery] Found via SRP: {Ip}:{Port}.", foundIp, foundPort);
            return new SrpDiscoveryResult(foundIp, foundPort);
        }
        return null;
    }

    [GeneratedRegex(@"^[0-9a-fA-F]+$")]
    private static partial Regex SafeHexPattern();

    [GeneratedRegex(@"\[?([0-9a-fA-F]*:[0-9a-fA-F:]{4,}[0-9a-fA-F])(%[^\]\s]+)?\]?")]
    private static partial Regex Ipv6AddressPattern();

    [GeneratedRegex(@"\[?([0-9a-fA-F]*:[0-9a-fA-F:]{4,}[0-9a-fA-F])(%[^\]]+)?\]?:(\d+)")]
    private static partial Regex Ipv6WithPortPattern();

    private static async Task<SrpDiscoveryResult?> TryDnsBrowseAsync(
        string compressedFabricId, string nodeOperationalId)
    {
        try
        {
            if (!SafeHexPattern().IsMatch(compressedFabricId) || !SafeHexPattern().IsMatch(nodeOperationalId))
            {
                MatterLog.Warn("[Discovery] Invalid fabric/node ID format — skipping DNS resolve");
                return null;
            }

            var serviceName = $"{compressedFabricId}-{nodeOperationalId}";
            var output = await RunOtCtlAsync($"dns resolve {serviceName}._matter._tcp.default.service.arpa");
            if (string.IsNullOrWhiteSpace(output) || output.Contains("Error"))
            {
                return null;
            }

            var ipMatch = Ipv6WithPortPattern().Match(output);
            if (ipMatch.Success && IPAddress.TryParse(ipMatch.Groups[1].Value, out var ip)
                && ushort.TryParse(ipMatch.Groups[3].Value, out ushort port))
            {
                MatterLog.Info("[Discovery] Found via DNS: {Ip}:{Port}.", ip, port);
                return new SrpDiscoveryResult(ip, port);
            }
        }
        catch (Exception ex)
        {
            MatterLog.Warn(ex, "[Discovery] DNS browse failed.");
        }
        return null;
    }

    private static Task<string> RunOtCtlAsync(string command)
        => OtbrHelper.RunOtCtlAsync(command);
}
