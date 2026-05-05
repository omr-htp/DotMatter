using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotMatter.Hosting;

/// <summary>
/// Shared helpers for invoking and parsing <c>ot-ctl</c> output.
/// </summary>
public static partial class OtbrCommandHelper
{
    /// <summary>Runs an <c>ot-ctl</c> command and returns either full output or the first meaningful line.</summary>
    public static async Task<string?> RunAsync(
        string commandPath,
        string? sudoCommand,
        string command,
        CancellationToken ct,
        bool firstLineOnly = true)
    {
        var startInfo = CreateStartInfo(commandPath, sudoCommand, command);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{startInfo.FileName}'.");

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return firstLineOnly ? GetFirstMeaningfulLine(output) : output;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    /// <summary>Extracts the first hexadecimal payload line from command output.</summary>
    public static string? ExtractHexPayload(string? output)
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

    /// <summary>Extracts the active SRP address for one Matter service name.</summary>
    public static string? ExtractSrpServiceAddress(string? output, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }

        return ExtractSrpAddressCore(output, serviceName);
    }

    /// <summary>Extracts the first active SRP service address from command output.</summary>
    public static string? ExtractFirstActiveSrpAddress(string? output)
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

        foreach (var arg in command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
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
