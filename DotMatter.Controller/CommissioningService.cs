using DotMatter.Core.Commissioning;
using DotMatter.Core.Fabrics;
using DotMatter.Core.LinuxBle;
using DotMatter.Hosting;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Math;
using System.Text.Json;
using System.Threading.Channels;

namespace DotMatter.Controller;

/// <summary>
/// Controller-specific commissioning result, extends Core's result with app-level DeviceId.
/// </summary>
public record ControllerCommissioningResult(
    bool Success,
    string? DeviceId,
    string? NodeId,
    string? ThreadIp,
    string? Error
);

/// <summary>
/// Thin orchestrator that delegates protocol work to <see cref="MatterCommissioner"/>
/// and handles platform-specific concerns (BLE, Thread dataset, persistence).
/// </summary>
public class CommissioningService(
    ILogger<CommissioningService> log,
    DeviceRegistry registry,
    IOtbrService otbrService,
    IOptions<CommissioningOptions> options)
{
    private readonly ILogger<CommissioningService> _log = log;
    private readonly DeviceRegistry _registry = registry;
    private readonly IOtbrService _otbrService = otbrService;
    private readonly CommissioningOptions _options = options.Value;
    private readonly Channel<string> _events = Channel.CreateUnbounded<string>();
    private readonly SemaphoreSlim _commissioning = new(1, 1);

    /// <summary>Gets the commissioning event stream.</summary>
    public ChannelReader<string> Events => _events.Reader;

    /// <summary>Commissions a Matter device over BLE.</summary>
    public async Task<ControllerCommissioningResult> CommissionDeviceAsync(
        int discriminator,
        uint passcode,
        string fabricName,
        CancellationToken ct,
        bool isShortDiscriminator = false,
        Action<CommissioningProgress>? onProgress = null)
    {
        var (success, deviceId, nodeId, ip, error) = await RunWithCommissioningLockAsync(
            () => CommissionCoreAsync(
                discriminator, passcode, fabricName,
                fetchThreadDataset: true,
                wifiSsid: null, wifiPassword: null, transport: null,
                isShortDiscriminator, onProgress, ct),
            ct);
        return new ControllerCommissioningResult(success, deviceId, nodeId, ip, error);
    }

    /// <summary>Commissions a Wi-Fi Matter device over BLE and provisions Wi-Fi credentials.</summary>
    public async Task<WifiCommissioningResult> CommissionWifiDeviceAsync(
        int discriminator,
        uint passcode,
        string fabricName,
        string wifiSsid,
        string wifiPassword,
        CancellationToken ct,
        bool isShortDiscriminator = false,
        Action<CommissioningProgress>? onProgress = null)
    {
        var (success, deviceId, nodeId, ip, error) = await RunWithCommissioningLockAsync(
            () => CommissionCoreAsync(
                discriminator, passcode, fabricName,
                fetchThreadDataset: false,
                wifiSsid, wifiPassword, transport: "wifi",
                isShortDiscriminator, onProgress, ct),
            ct);
        return new WifiCommissioningResult(success, deviceId, nodeId, ip, error);
    }

    /// <summary>Runs the shared commissioning workflow.</summary>
    protected virtual async Task<(bool Success, string? DeviceId, string? NodeId, string? Ip, string? Error)> CommissionCoreAsync(
        int discriminator,
        uint passcode,
        string fabricName,
        bool fetchThreadDataset,
        string? wifiSsid,
        string? wifiPassword,
        string? transport,
        bool isShortDiscriminator,
        Action<CommissioningProgress>? onProgress,
        CancellationToken ct)
    {
        LinuxBTPConnection? bleConnection = null;
        try
        {
            var progress = onProgress ?? (_ => { });
            fabricName = string.IsNullOrWhiteSpace(fabricName)
                ? $"{_options.DefaultFabricNamePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}"
                : fabricName;

            EnsureSharedFabricMaterial(fabricName);
            IFabricStorageProvider fabricStorage = new FabricDiskStorage(_registry.BasePath);
            var fabricManager = new FabricManager(fabricStorage);
            var fabric = await fabricManager.GetAsync(fabricName);

            bleConnection = CreateBleConnection();
            var connected = await bleConnection.ConnectAsync(discriminator, isShortDiscriminator, ct);
            if (!connected)
            {
                return (false, null, null, null, "BTP connection failed — device not found or handshake error");
            }

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("BTP connected (discriminator={Disc})", discriminator);
            }

            byte[]? threadDataset = null;
            if (fetchThreadDataset)
            {
                var datasetHex = await _otbrService.GetActiveDatasetHexAsync(ct);
                if (string.IsNullOrWhiteSpace(datasetHex))
                {
                    return (false, null, null, null, "Failed to parse Thread operational dataset from OTBR");
                }

                threadDataset = Convert.FromHexString(datasetHex);
            }

            var result = await ExecuteCommissioningAsync(
                bleConnection,
                fabric,
                passcode,
                threadDataset,
                wifiSsid,
                wifiPassword,
                p =>
                {
                    progress(p);
                    PublishEvent("commissioning", p.Step, p.Message);
                },
                ct);

            bleConnection.Close();
            bleConnection = null;

            if (!result.Success)
            {
                return (false, null, result.NodeId, result.ThreadIp, result.Error);
            }

            string? operationalIp = result.ThreadIp;
            if (fetchThreadDataset && string.IsNullOrWhiteSpace(operationalIp))
            {
                operationalIp = await ResolveCommissionedThreadIpAsync(fabric, result.NodeId, ct)
                    ?? await _otbrService.DiscoverThreadIpAsync(_log, ct);
            }

            var deviceId = fabricName;
            var fabricDir = Path.Combine(_registry.BasePath, fabricName);
            Directory.CreateDirectory(fabricDir);

            var nodeInfo = new NodeInfoRecord(
                result.NodeId ?? "",
                operationalIp ?? "",
                fabricName,
                Commissioned: DateTime.UtcNow,
                Transport: transport);

            await AtomicFilePersistence.WriteTextAsync(
                Path.Combine(fabricDir, "node_info.json"),
                JsonSerializer.Serialize(nodeInfo, HostingJsonIndentedContext.Default.NodeInfoRecord),
                ct);

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("Commissioning complete: DeviceId={DeviceId}, NodeId={NodeId}, IP={Ip}",
                    deviceId, result.NodeId, operationalIp);
            }

            PublishEvent(deviceId, "commissioned", fabricName);

            return (true, deviceId, result.NodeId, operationalIp, null);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("Commissioning cancelled");
            return (false, null, null, null, "Commissioning was cancelled");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Commissioning failed");
            return (false, null, null, null, $"Commissioning failed: {ex.Message}");
        }
        finally
        {
            try
            {
                bleConnection?.Close();
            }
            catch
            {
            }
        }
    }

    /// <summary>Creates the BLE transport used for commissioning.</summary>
    protected virtual LinuxBTPConnection CreateBleConnection() => new();

    private void EnsureSharedFabricMaterial(string fabricName)
    {
        if (string.Equals(fabricName, _options.SharedFabricName, StringComparison.Ordinal))
        {
            return;
        }

        var targetDir = Path.Combine(_registry.BasePath, fabricName);
        if (File.Exists(Path.Combine(targetDir, "fabric.json")))
        {
            return;
        }

        var sharedDir = Path.Combine(_registry.BasePath, _options.SharedFabricName);
        if (!File.Exists(Path.Combine(sharedDir, "fabric.json")))
        {
            return;
        }

        Directory.CreateDirectory(targetDir);
        foreach (var fileName in new[]
        {
            "fabric.json",
            "rootCertificate.pem",
            "rootKeyPair.pem",
            "operationalCertificate.pem",
            "operationalKeyPair.pem",
        })
        {
            var source = Path.Combine(sharedDir, fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Shared fabric file '{fileName}' was not found in {sharedDir}", source);
            }

            File.Copy(source, Path.Combine(targetDir, fileName), overwrite: false);
        }

        _log.LogInformation(
            "Copied shared fabric material from {SharedFabric} to {FabricName}",
            _options.SharedFabricName,
            fabricName);
    }

    /// <summary>Executes the Matter commissioning protocol over an established BLE connection.</summary>
    protected virtual Task<CommissioningResult> ExecuteCommissioningAsync(
        LinuxBTPConnection bleConnection,
        Fabric fabric,
        uint passcode,
        byte[]? threadDataset,
        string? wifiSsid,
        string? wifiPassword,
        Action<CommissioningProgress> onProgress,
        CancellationToken ct)
    {
        var commissioner = new MatterCommissioner(_log);
        return commissioner.CommissionAsync(
            bleConnection,
            fabric,
            passcode,
            threadDataset,
            wifiSsid: wifiSsid,
            wifiPassword: wifiPassword,
            onProgress: onProgress,
            ct: ct);
    }

    private void PublishEvent(string source, string type, string value)
    {
        var json = JsonSerializer.Serialize(new CommissionEvent(source, type, value, DateTime.UtcNow), ControllerJsonContext.Default.CommissionEvent);
        _events.Writer.TryWrite(json);
    }

    private async Task<(bool Success, string? DeviceId, string? NodeId, string? Ip, string? Error)> RunWithCommissioningLockAsync(
        Func<Task<(bool Success, string? DeviceId, string? NodeId, string? Ip, string? Error)>> action,
        CancellationToken ct)
    {
        DotMatterProductDiagnostics.RecordCommissioningAttempt();

        if (!await _commissioning.WaitAsync(TimeSpan.Zero, ct))
        {
            DotMatterProductDiagnostics.RecordCommissioningRejection();
            return (false, null, null, null, "Another commissioning is already in progress");
        }

        try
        {
            return await action();
        }
        finally
        {
            _commissioning.Release();
        }
    }

    private async Task<string?> ResolveCommissionedThreadIpAsync(Fabric fabric, string? nodeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        try
        {
            var nodeOperationalId = MatterDeviceHost.GetNodeOperationalId(new BigInteger(nodeId));
            var serviceName = $"{fabric.CompressedFabricId}-{nodeOperationalId}";
            var ip = await _otbrService.ResolveSrpServiceAddressAsync(serviceName, ct);

            if (!string.IsNullOrWhiteSpace(ip) && _log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("Resolved commissioned Thread node via SRP: {Service} -> {Ip}", serviceName, ip);
            }

            return ip;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Specific SRP lookup failed for newly commissioned node.");
            return null;
        }
    }
}
