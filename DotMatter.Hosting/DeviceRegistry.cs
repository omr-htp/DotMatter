using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DotMatter.Hosting;

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
    /// Reads node_info.json from each fabric subdirectory.
    /// </summary>
    public virtual void LoadFromDisk()
    {
        if (!Directory.Exists(_basePath))
        {
            return;
        }

        foreach (var fabricDir in Directory.GetDirectories(_basePath))
        {
            var nodeInfoPath = Path.Combine(fabricDir, "node_info.json");
            if (!File.Exists(nodeInfoPath))
            {
                continue;
            }

            try
            {
                var json = ReadNodeInfoJson(nodeInfoPath);
                var ni = JsonSerializer.Deserialize(json, HostingJsonContext.Default.NodeInfoRecord);
                if (ni is null || string.IsNullOrEmpty(ni.NodeId))
                {
                    continue;
                }

                var fabricName = Path.GetFileName(fabricDir);
                var id = MapFabricNameToDeviceId(fabricName);

                var info = new DeviceInfo
                {
                    Id = id,
                    Name = ni.DeviceName ?? fabricName,
                    NodeId = ni.NodeId,
                    Ip = ni.ThreadIPv6 ?? "",
                    Port = 5540,
                    FabricName = fabricName,
                    Transport = ni.Transport,
                };

                // Only add new devices; don't overwrite existing runtime state on re-load
                if (!_devices.TryAdd(id, info))
                {
                    continue;
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

    /// <summary>Removes a device from the registry and deletes its persisted fabric directory.</summary>
    public bool Remove(string id)
    {
        if (!_devices.TryRemove(id, out var device))
        {
            return false;
        }

        var fabricDir = Path.Combine(_basePath, device.FabricName);
        if (Directory.Exists(fabricDir))
        {
            try
            {
                Directory.Delete(fabricDir, recursive: true);
                if (_log.IsEnabled(LogLevel.Information))
                {
                    _log.LogInformation("Deleted fabric directory {Dir}", fabricDir);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete fabric directory {Dir}", fabricDir);
            }
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
    public void PersistIp(string id, string ip)
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

            ni = ni with { ThreadIPv6 = ip };
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
    /// Hook for host-specific diagnostics when registry persistence fails.
    /// </summary>
    protected virtual void OnPersistenceFailure(string id, string operation, Exception exception)
    {
    }

    private string GetNodeInfoPath(DeviceInfo device)
        => Path.Combine(_basePath, device.FabricName, "node_info.json");

    private static string ReadNodeInfoJson(string path)
        => File.ReadAllText(path).TrimStart('\uFEFF');
}
