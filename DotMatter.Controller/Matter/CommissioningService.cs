using System.Text.Json;
using System.Threading.Channels;
using DotMatter.Controller.Configuration;
using DotMatter.Controller.Diagnostics;
using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Core.Commissioning;
using DotMatter.Core.Discovery;
using DotMatter.Core.Fabrics;
using DotMatter.Core.LinuxBle;
using DotMatter.Hosting;
using DotMatter.Hosting.Commissioning;
using DotMatter.Hosting.Devices;
using DotMatter.Hosting.Runtime;
using DotMatter.Hosting.Storage;
using DotMatter.Hosting.Thread;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Math;

namespace DotMatter.Controller.Matter;

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
        bool provisionThreadNetwork = true,
        Action<CommissioningProgress>? onProgress = null)
    {
        return await RunWithCommissioningLockAsync(
            () => CommissionCoreAsync(
                discriminator, passcode, fabricName,
                fetchThreadDataset: provisionThreadNetwork,
                wifiSsid: null,
                wifiPassword: null,
                transport: provisionThreadNetwork ? "thread" : "on-network",
                isShortDiscriminator, onProgress, ct),
            () => new ControllerCommissioningResult(false, null, null, null, "Another commissioning is already in progress"),
            ct,
            recordAttempt: true);
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
        var result = await RunWithCommissioningLockAsync(
            () => CommissionCoreAsync(
                discriminator, passcode, fabricName,
                fetchThreadDataset: false,
                wifiSsid, wifiPassword, transport: "wifi",
                isShortDiscriminator, onProgress, ct),
            () => new ControllerCommissioningResult(false, null, null, null, "Another commissioning is already in progress"),
            ct,
            recordAttempt: true);
        return new WifiCommissioningResult(result.Success, result.DeviceId, result.NodeId, result.ThreadIp, result.Error);
    }

    /// <summary>Runs the shared commissioning workflow.</summary>
    protected virtual async Task<ControllerCommissioningResult> CommissionCoreAsync(
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
            var deviceId = string.IsNullOrWhiteSpace(fabricName)
                ? MatterFabricNames.Normalize($"{_options.DefaultFabricNamePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}")
                : MatterFabricNames.Normalize(fabricName);
            var sharedFabricName = MatterFabricNames.Normalize(_options.SharedFabricName);

            if (string.Equals(deviceId, sharedFabricName, StringComparison.Ordinal))
            {
                return new ControllerCommissioningResult(
                    false,
                    deviceId,
                    null,
                    null,
                    $"Device id '{deviceId}' is reserved for the shared Matter fabric. Choose a unique device name before commissioning.");
            }

            if (_registry.Get(deviceId) is not null)
            {
                return new ControllerCommissioningResult(
                    false,
                    deviceId,
                    null,
                    null,
                    $"Device id '{deviceId}' already exists. Delete the existing device first or choose a unique name before commissioning.");
            }

            var threadDatasetResult = await ReadThreadDatasetBeforeDeviceConnectionAsync(fetchThreadDataset, ct);
            if (threadDatasetResult.Error is not null)
            {
                return new ControllerCommissioningResult(false, null, null, null, threadDatasetResult.Error);
            }

            await EnsureSharedFabricMaterialAsync(sharedFabricName);
            IFabricStorageProvider fabricStorage = new FabricDiskStorage(_registry.BasePath);
            var fabricManager = new FabricManager(fabricStorage);
            var fabric = await fabricManager.GetAsync(sharedFabricName);

            bleConnection = CreateBleConnection();
            var connected = await bleConnection.ConnectAsync(discriminator, isShortDiscriminator, ct);
            if (!connected)
            {
                return new ControllerCommissioningResult(false, null, null, null, "BTP connection failed — device not found or handshake error");
            }

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("BTP connected (discriminator={Disc})", discriminator);
            }

            var result = await ExecuteCommissioningAsync(
                bleConnection,
                fabric,
                passcode,
                threadDatasetResult.Dataset,
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
                return new ControllerCommissioningResult(false, null, result.NodeId, result.ThreadIp, result.Error);
            }

            string? operationalIp = result.ThreadIp;
            ushort operationalPort = result.OperationalPort ?? 5540;
            if (string.IsNullOrWhiteSpace(operationalIp))
            {
                var operationalNode = await ResolveCommissionedOperationalNodeAsync(fabric, result.NodeId, ct);
                if (operationalNode != null)
                {
                    operationalIp = operationalNode.Address.ToString();
                    operationalPort = (ushort)operationalNode.Port;
                }
            }

            if (fetchThreadDataset && string.IsNullOrWhiteSpace(operationalIp))
            {
                operationalIp = await ResolveCommissionedThreadIpAsync(fabric, result.NodeId, ct)
                    ?? await _otbrService.DiscoverThreadIpAsync(_log, ct);
            }

            if (string.IsNullOrWhiteSpace(operationalIp))
            {
                var networkPath = fetchThreadDataset
                    ? "Thread"
                    : string.IsNullOrWhiteSpace(wifiSsid) ? "already-on-network" : "Wi-Fi";
                var message = $"Commissioning completed fabric-add, but operational discovery failed for the {networkPath} path. The device was not added to the registry because no operational address was found.";
                _log.LogWarning(
                    "Commissioning incomplete: DeviceId={DeviceId}, NodeId={NodeId}, Transport={Transport}. {Message}",
                    deviceId,
                    result.NodeId,
                    transport,
                    message);
                return new ControllerCommissioningResult(false, deviceId, result.NodeId, null, message);
            }

            var nodeInfo = new NodeInfoRecord(
                result.NodeId ?? "",
                operationalIp,
                sharedFabricName,
                DeviceName: deviceId,
                Commissioned: DateTime.UtcNow,
                Transport: transport,
                OperationalPort: operationalPort);

            await MatterCommissioningStorage.WriteDeviceNodeInfoAsync(_registry.BasePath, sharedFabricName, deviceId, nodeInfo, ct);

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("Commissioning complete: DeviceId={DeviceId}, NodeId={NodeId}, Endpoint={Ip}:{Port}",
                    deviceId, result.NodeId, operationalIp, operationalPort);
            }

            PublishEvent(deviceId, "commissioned", deviceId);

            return new ControllerCommissioningResult(true, deviceId, result.NodeId, operationalIp, null);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("Commissioning cancelled");
            return new ControllerCommissioningResult(false, null, null, null, "Commissioning was cancelled");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Commissioning failed");
            return new ControllerCommissioningResult(false, null, null, null, $"Commissioning failed: {ex.Message}");
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

    private async Task<(byte[]? Dataset, string? Error)> ReadThreadDatasetBeforeDeviceConnectionAsync(
        bool fetchThreadDataset,
        CancellationToken ct)
    {
        if (!fetchThreadDataset)
        {
            return (null, null);
        }

        var datasetHex = await _otbrService.GetActiveDatasetHexAsync(ct);
        if (string.IsNullOrWhiteSpace(datasetHex))
        {
            return (null, "OTBR has no active Thread operational dataset. Start OTBR Thread first (`sudo ot-ctl dataset init new`, `sudo ot-ctl dataset commit active`, `sudo ot-ctl ifconfig up`, `sudo ot-ctl thread start`) or choose Wi-Fi/already-on-network commissioning.");
        }

        if (datasetHex.Length % 2 != 0)
        {
            return (null, "OTBR returned an invalid Thread operational dataset: hexadecimal payload length must be even.");
        }

        try
        {
            return (Convert.FromHexString(datasetHex), null);
        }
        catch (FormatException)
        {
            return (null, "OTBR returned an invalid Thread operational dataset: payload must be hexadecimal.");
        }
    }

    private async Task EnsureSharedFabricMaterialAsync(string sharedFabricName)
    {
        var sharedDir = Path.Combine(_registry.BasePath, sharedFabricName);
        if (!File.Exists(Path.Combine(sharedDir, "fabric.json")))
        {
            IFabricStorageProvider fabricStorage = new FabricDiskStorage(_registry.BasePath);
            var fabricManager = new FabricManager(fabricStorage);
            await fabricManager.GetAsync(sharedFabricName);
        }
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
            regulatoryLocation: MapRegulatoryLocation(_options.RegulatoryLocation),
            regulatoryCountryCode: _options.RegulatoryCountryCode,
            attestationVerifier: CreateAttestationVerifier(_options.AttestationPolicy),
            onProgress: onProgress,
            ct: ct);
    }

    private void PublishEvent(string source, string type, string value)
    {
        var json = JsonSerializer.Serialize(new CommissionEvent(source, type, value, DateTime.UtcNow), ControllerJsonContext.Default.CommissionEvent);
        _events.Writer.TryWrite(json);
    }

    private async Task<T> RunWithCommissioningLockAsync<T>(
        Func<Task<T>> action,
        Func<T> onRejected,
        CancellationToken ct,
        bool recordAttempt = false)
    {
        if (recordAttempt)
        {
            DotMatterProductDiagnostics.RecordCommissioningAttempt();
        }

        if (!await _commissioning.WaitAsync(TimeSpan.Zero, ct))
        {
            if (recordAttempt)
            {
                DotMatterProductDiagnostics.RecordCommissioningRejection();
            }

            return onRejected();
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

    private async Task<OperationalNode?> ResolveCommissionedOperationalNodeAsync(Fabric fabric, string? nodeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        try
        {
            var nodeIdBytes = new BigInteger(nodeId).ToByteArrayUnsigned();
            var nodeIdPadded = new byte[8];
            if (nodeIdBytes.Length <= 8)
            {
                Array.Copy(nodeIdBytes, 0, nodeIdPadded, 8 - nodeIdBytes.Length, nodeIdBytes.Length);
            }

            using var discovery = new OperationalDiscovery();
            var node = await discovery.ResolveNodeAsync(
                Convert.ToUInt64(fabric.CompressedFabricId, 16),
                BitConverter.ToUInt64(nodeIdPadded),
                TimeSpan.FromSeconds(15));

            if (node != null)
            {
                _log.LogInformation("Resolved commissioned operational node via mDNS: {Ip}:{Port}", node.Address, node.Port);
                return node;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Operational mDNS lookup failed for newly commissioned node.");
        }

        return null;
    }

    private static GeneralCommissioningCluster.RegulatoryLocationTypeEnum MapRegulatoryLocation(
        CommissioningRegulatoryLocation location)
        => location switch
        {
            CommissioningRegulatoryLocation.Indoor => GeneralCommissioningCluster.RegulatoryLocationTypeEnum.Indoor,
            CommissioningRegulatoryLocation.Outdoor => GeneralCommissioningCluster.RegulatoryLocationTypeEnum.Outdoor,
            _ => GeneralCommissioningCluster.RegulatoryLocationTypeEnum.IndoorOutdoor,
        };

    private static DefaultDeviceAttestationVerifier? CreateAttestationVerifier(CommissioningAttestationPolicy policy)
        => policy switch
        {
            CommissioningAttestationPolicy.Disabled => null,
            CommissioningAttestationPolicy.RequireDacChain => new DefaultDeviceAttestationVerifier(),
            CommissioningAttestationPolicy.AllowTestDevices => new DefaultDeviceAttestationVerifier(allowTestDevices: true),
            _ => throw new InvalidOperationException($"Unsupported attestation policy '{policy}'.")
        };
}
