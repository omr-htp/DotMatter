using DotMatter.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DotMatter.Hosting;

/// <summary>
/// Default OTBR integration based on invoking <c>ot-ctl</c>.
/// </summary>
public sealed class OtbrService(
    ILogger<OtbrService> log,
    IOptions<OtbrServiceOptions> options) : IOtbrService
{
    private readonly ILogger<OtbrService> _log = log;
    private readonly OtbrServiceOptions _options = options.Value;

    /// <inheritdoc />
    public async Task EnableSrpServerAsync(CancellationToken ct)
    {
        if (!_options.EnableSrpServerOnStartup)
        {
            return;
        }

        var exitCode = await RunProcessAsync("srp server enable", ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to enable OTBR SRP server (exit code {exitCode}).");
        }
    }

    /// <inheritdoc />
    public Task<string?> RunOtCtlAsync(string command, CancellationToken ct, bool firstLineOnly = true)
        => OtbrHelper.RunOtCtlAsync(_options.CommandPath, _options.SudoCommand, command, ct, firstLineOnly);

    /// <inheritdoc />
    public async Task<string?> GetActiveDatasetHexAsync(CancellationToken ct)
    {
        var datasetOutput = await RunOtCtlAsync("dataset active -x", ct, firstLineOnly: false);
        return OtbrHelper.ExtractHexPayload(datasetOutput);
    }

    /// <inheritdoc />
    public async Task<string?> ResolveSrpServiceAddressAsync(string serviceName, CancellationToken ct)
    {
        var srpOutput = await RunOtCtlAsync("srp server service", ct, firstLineOnly: false);
        return OtbrHelper.ExtractSrpServiceAddress(srpOutput, serviceName);
    }

    /// <inheritdoc />
    public async Task<string?> DiscoverThreadIpAsync(ILogger log, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= _options.ThreadIpDiscoveryMaxAttempts; attempt++)
        {
            try
            {
                var srpOutput = await RunOtCtlAsync("srp server service", ct, firstLineOnly: false);
                var ip = OtbrHelper.ExtractFirstActiveSrpAddress(srpOutput);
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return ip;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogDebug(ex, "[MATTER] Thread IP discovery attempt {Attempt} failed", attempt);
            }

            if (attempt < _options.ThreadIpDiscoveryMaxAttempts)
            {
                log.LogDebug("[MATTER] Thread IP not found, retrying ({N}/{Max})...", attempt, _options.ThreadIpDiscoveryMaxAttempts);
                await Task.Delay(_options.ThreadIpDiscoveryDelay, ct);
            }
        }

        log.LogWarning("[MATTER] Thread IP discovery failed after {Max} attempts", _options.ThreadIpDiscoveryMaxAttempts);
        return null;
    }

    private async Task<int> RunProcessAsync(string command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_options.SudoCommand) ? _options.CommandPath : _options.SudoCommand,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(_options.SudoCommand))
        {
            psi.ArgumentList.Add(_options.CommandPath);
        }

        foreach (var arg in command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) 
            ?? throw new InvalidOperationException($"Failed to start process '{psi.FileName}'.");

        try
        {
            await process.WaitForExitAsync(ct);
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
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to kill cancelled OTBR command process.");
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _log.LogWarning("OTBR command failed: {Message}", stderr.Trim());
            }
        }

        return process.ExitCode;
    }
}
