using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotMatter.Core;

/// <summary>
/// Utility for invoking ot-ctl commands on the OTBR host.
/// </summary>
public static partial class OtbrHelper
{
    /// <summary>RunOtCtlAsync.</summary>
    public static async Task<string?> RunOtCtlAsync(string command, CancellationToken ct, bool firstLineOnly = true)
        => await RunOtCtlAsync("ot-ctl", "sudo", command, ct, firstLineOnly);

    internal static async Task<string?> RunOtCtlAsync(
        string commandPath,
        string? sudoCommand,
        string command,
        CancellationToken ct,
        bool firstLineOnly = true)
    {
        var startInfo = CreateStartInfo(commandPath, sudoCommand, command);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (!firstLineOnly)
            {
                return output;
            }

            return GetFirstMeaningfulLine(output);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch
            {
            }
            throw;
        }
    }

    internal static string? ExtractHexPayload(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(line) || string.Equals(line, "Done", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.AsSpan().IndexOfAnyExcept("0123456789abcdefABCDEF".AsSpan()) < 0)
            {
                return line;
            }
        }

        return null;
    }

    internal static string? ExtractSrpServiceAddress(string? output, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }

        return ExtractSrpAddressCore(output, serviceName);
    }

    internal static string? ExtractFirstActiveSrpAddress(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        string? currentServiceName = null;
        var deleted = false;
        foreach (var rawLine in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || string.Equals(line, "Done", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Contains("._matter._tcp", StringComparison.OrdinalIgnoreCase))
            {
                currentServiceName = line.Split('.', 2)[0];
                deleted = false;
                continue;
            }

            if (currentServiceName is null)
            {
                continue;
            }

            if (line.StartsWith("deleted:", StringComparison.OrdinalIgnoreCase))
            {
                deleted = line.Contains("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!deleted && line.StartsWith("addresses:", StringComparison.OrdinalIgnoreCase))
            {
                var address = ExtractIpv6Address(line);
                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Convenience overload with a built-in timeout. Returns full output or "" on timeout.
    /// </summary>
    public static async Task<string> RunOtCtlAsync(string command, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        try
        {
            return await RunOtCtlAsync(command, cts.Token, firstLineOnly: false) ?? "";
        }
        catch (OperationCanceledException)
        {
            MatterLog.Warn("[OTBR] ot-ctl command timed out: {Command}", command);
            return "";
        }
    }

    private static ProcessStartInfo CreateStartInfo(string commandPath, string? sudoCommand, string command)
    {
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            throw new ArgumentException("OTBR command path is required.", nameof(commandPath));
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("OTBR command is required.", nameof(command));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(sudoCommand) ? commandPath : sudoCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(sudoCommand))
        {
            startInfo.ArgumentList.Add(commandPath);
        }

        AddCommandArguments(startInfo, command);
        return startInfo;
    }

    private static void AddCommandArguments(ProcessStartInfo startInfo, string command)
    {
        foreach (var arg in command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            startInfo.ArgumentList.Add(arg);
        }
    }

    private static string? GetFirstMeaningfulLine(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(line) && !string.Equals(line, "Done", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return null;
    }

    private static string? ExtractSrpAddressCore(string output, string serviceName)
    {
        var inBlock = false;
        var deleted = false;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || string.Equals(line, "Done", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Contains("._matter._tcp", StringComparison.OrdinalIgnoreCase))
            {
                var currentServiceName = line.Split('.', 2)[0];
                inBlock = string.Equals(currentServiceName, serviceName, StringComparison.OrdinalIgnoreCase);
                deleted = false;
                continue;
            }

            if (!inBlock)
            {
                continue;
            }

            if (line.StartsWith("deleted:", StringComparison.OrdinalIgnoreCase))
            {
                deleted = line.Contains("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!deleted && line.StartsWith("addresses:", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractIpv6Address(line);
            }
        }

        return null;
    }

    private static string? ExtractIpv6Address(string line)
    {
        var match = Ipv6Pattern().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"([0-9a-fA-F]*:[0-9a-fA-F:]{2,}[0-9a-fA-F])")]
    private static partial Regex Ipv6Pattern();
}
