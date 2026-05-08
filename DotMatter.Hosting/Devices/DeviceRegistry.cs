using System.Collections.Concurrent;
using System.Text.Json;
using DotMatter.Core.Clusters;
using DotMatter.Hosting.Commissioning;
using DotMatter.Hosting.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotMatter.Hosting.Devices;

/// <summary>
/// Thread-safe registry of commissioned Matter devices.
/// Loads/persists device info from fabric directories on disk.
/// </summary>
public class DeviceRegistry(ILogger logger, string? basePath = null)
{
    private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();
    private readonly ILogger _log = logger;
    private readonly string _basePath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dot-matter", "fabrics");

    /// <summary>Gets the fabric directory base path.</summary>
    public string BasePath => _basePath;

    /// <summary>Gets the registered device IDs.</summary>
    public IEnumerable<string> DeviceIds => _devices.Keys;

    /// <summary>Returns all registered devices.</summary>
    public IEnumerable<DeviceInfo> GetAll() => _devices.Values;

    /// <summary>Initializes a new registry from configured options.</summary>
    public DeviceRegistry(ILogger<DeviceRegistry> logger, IOptions<RegistryOptions> options)
        : this(logger, options.Value.BasePath)
    {
    }

    /// <summary>Gets a registered device by ID.</summary>
    public DeviceInfo? Get(string id) =>
        _devices.TryGetValue(id, out var d) ? d : null;

    /// <summary>
    /// Maps a fabric directory name to a device ID. Override to add a prefix or custom mapping.
    /// </summary>
    protected virtual string MapFabricNameToDeviceId(string fabricName) => fabricName;

    /// <summary>
    /// Load all devices from fabric directories on disk.
    /// Reads shared-fabric device records first, then legacy per-device node_info.json files.
    /// </summary>
    public virtual void LoadFromDisk()
    {
        if (!Directory.Exists(_basePath))
        {
            return;
        }

        foreach (var fabricDir in Directory.GetDirectories(_basePath))
        {
            var fabricName = Path.GetFileName(fabricDir);
            var devicesDir = Path.Combine(fabricDir, MatterCommissioningStorage.DeviceNodeInfoDirectoryName);
            if (Directory.Exists(devicesDir))
            {
                foreach (var deviceInfoPath in Directory.GetFiles(devicesDir, "*.json"))
                {
                    TryLoadNodeInfo(fabricName, Path.GetFileNameWithoutExtension(deviceInfoPath), deviceInfoPath);
                }
            }

            var nodeInfoPath = Path.Combine(fabricDir, "node_info.json");
            if (File.Exists(nodeInfoPath))
            {
                TryLoadNodeInfo(fabricName, MapFabricNameToDeviceId(fabricName), nodeInfoPath);
            }
        }

        if (_log.IsEnabled(LogLevel.Information))
        {
            _log.LogInformation("Registry: {Count} device(s) loaded", _devices.Count);
        }
    }

    /// <summary>Registers or replaces a device entry.</summary>
    public void Register(DeviceInfo device)
    {
        _devices[device.Id] = device;
        if (_log.IsEnabled(LogLevel.Information))
        {
            _log.LogInformation("Registered device {Id}: NodeId={NodeId}, IP={Ip}",
                device.Id, device.NodeId, device.Ip);
        }
    }

    /// <summary>Updates a registered device if it exists.</summary>
    public void Update(string id, Action<DeviceInfo> updater)
    {
        if (_devices.TryGetValue(id, out var d))
        {
            d.Mutate(updater);
        }
    }

    private void TryLoadNodeInfo(string fabricName, string id, string nodeInfoPath)
    {
        try
        {
            var json = ReadNodeInfoJson(nodeInfoPath);
            var ni = JsonSerializer.Deserialize(json, HostingJsonContext.Default.NodeInfoRecord);
            if (ni is null || string.IsNullOrEmpty(ni.NodeId))
            {
                return;
            }

            var info = new DeviceInfo
            {
                Id = id,
                Name = ni.DeviceName ?? id,
                NodeId = ni.NodeId,
                Ip = ni.ThreadIPv6 ?? "",
                Port = ni.OperationalPort ?? 5540,
                FabricName = fabricName,
                Transport = ni.Transport,
                VendorName = ni.VendorName,
                ProductName = ni.ProductName,
                DeviceType = string.IsNullOrWhiteSpace(ni.DeviceType) ? "on_off_light" : ni.DeviceType,
                ColorCapabilities = ni.ColorCapabilities.HasValue
                    ? (ColorControlCluster.ColorCapabilitiesBitmap)ni.ColorCapabilities.Value
                    : default,
                Endpoints = ni.Endpoints?.ToDictionary(
                    static entry => entry.Key,
                    static entry => entry.Value.ToList())
            };

            if (!_devices.TryAdd(id, info))
            {
                return;
            }

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("Loaded device {Id}: NodeId={NodeId}, IP={Ip}", id, ni.NodeId, ni.ThreadIPv6 ?? "?");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load node info from {Path}", nodeInfoPath);
        }
    }

    /// <summary>Removes a device from the registry and deletes its persisted metadata.</summary>
    public bool Remove(string id)
    {
        if (!_devices.TryRemove(id, out var device))
        {
            return false;
        }

        try
        {
            if (!string.Equals(device.Id, device.FabricName, StringComparison.Ordinal))
            {
                var nodeInfoPath = GetNodeInfoPath(device);
                if (File.Exists(nodeInfoPath))
                {
                    File.Delete(nodeInfoPath);
                }

                var devicesDir = Path.GetDirectoryName(nodeInfoPath);
                if (devicesDir is not null
                    && Directory.Exists(devicesDir)
                    && !Directory.EnumerateFileSystemEntries(devicesDir).Any())
                {
                    Directory.Delete(devicesDir);
                }

                if (_log.IsEnabled(LogLevel.Information))
                {
                    _log.LogInformation("Deleted device metadata {Path}", nodeInfoPath);
                }
            }
            else
            {
                var fabricDir = GetFabricPath(device.FabricName);
                if (Directory.Exists(fabricDir))
                {
                    Directory.Delete(fabricDir, recursive: true);
                    if (_log.IsEnabled(LogLevel.Information))
                    {
                        _log.LogInformation("Deleted fabric directory {Dir}", fabricDir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to delete fabric directory for {FabricName}", device.FabricName);
        }

        if (_log.IsEnabled(LogLevel.Information))
        {
            _log.LogInformation("Removed device {Id}", id);
        }

        return true;
    }

    /// <summary>
    /// Persist updated IP address to node_info.json.
    /// </summary>
    public void PersistIp(string id, string ip, ushort? port = null)
    {
        var device = Get(id);
        if (device is null)
        {
            return;
        }

        try
        {
            var nodeInfoPath = GetNodeInfoPath(device);
            if (!File.Exists(nodeInfoPath))
            {
                return;
            }

            var ni = JsonSerializer.Deserialize(
                ReadNodeInfoJson(nodeInfoPath), HostingJsonContext.Default.NodeInfoRecord);
            if (ni is null)
            {
                return;
            }

            ni = ni with
            {
                ThreadIPv6 = ip,
                OperationalPort = port ?? ni.OperationalPort
            };
            AtomicFilePersistence.WriteText(
                nodeInfoPath,
                JsonSerializer.Serialize(ni, HostingJsonIndentedContext.Default.NodeInfoRecord));
        }
        catch (Exception ex)
        {
            OnPersistenceFailure(id, "ip-address", ex);
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug(ex, "Device {Id}: failed to persist IP.", id);
            }
        }
    }

    /// <summary>
    /// Persist cached device metadata used for capability-aware UI rendering.
    /// </summary>
    public void PersistDeviceCache(string id)
    {
        var device = Get(id);
        if (device is null)
        {
            return;
        }

        try
        {
            var nodeInfoPath = GetNodeInfoPath(device);
            var existing = File.Exists(nodeInfoPath)
                ? JsonSerializer.Deserialize(ReadNodeInfoJson(nodeInfoPath), HostingJsonContext.Default.NodeInfoRecord)
                : null;

            var record = (existing ?? new NodeInfoRecord(
                device.NodeId,
                string.IsNullOrWhiteSpace(device.Ip) ? null : device.Ip,
                device.FabricName,
                device.Name,
                device.Transport))
                with
            {
                NodeId = device.NodeId,
                ThreadIPv6 = string.IsNullOrWhiteSpace(device.Ip) ? null : device.Ip,
                FabricName = device.FabricName,
                DeviceName = device.Name,
                Transport = device.Transport,
                OperationalPort = (ushort)device.Port,
                VendorName = device.VendorName,
                ProductName = device.ProductName,
                DeviceType = string.IsNullOrWhiteSpace(device.DeviceType) ? null : device.DeviceType,
                ColorCapabilities = (ushort)device.ColorCapabilities,
                Endpoints = device.Endpoints?.ToDictionary(
                        static entry => entry.Key,
                        static entry => entry.Value.ToList())
            };

            AtomicFilePersistence.WriteText(
                nodeInfoPath,
                JsonSerializer.Serialize(record, HostingJsonIndentedContext.Default.NodeInfoRecord));
        }
        catch (Exception ex)
        {
            OnPersistenceFailure(id, "device-cache", ex);
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug(ex, "Device {Id}: failed to persist cached capability metadata.", id);
            }
        }
    }

    /// <summary>
    /// Hook for host-specific diagnostics when registry persistence fails.
    /// </summary>
    protected virtual void OnPersistenceFailure(string id, string operation, Exception exception)
    {
    }

    private string GetNodeInfoPath(DeviceInfo device)
    {
        var sharedDevicePath = MatterCommissioningStorage.GetDeviceNodeInfoPath(_basePath, device.FabricName, device.Id);
        return !string.Equals(device.Id, device.FabricName, StringComparison.Ordinal) || File.Exists(sharedDevicePath)
            ? sharedDevicePath
            : Path.Combine(GetFabricPath(device.FabricName), "node_info.json");
    }

    private string GetFabricPath(string fabricName)
        => MatterFabricNames.GetFabricPath(_basePath, fabricName);

    private static string ReadNodeInfoJson(string path)
        => File.ReadAllText(path).TrimStart('\uFEFF');
}
