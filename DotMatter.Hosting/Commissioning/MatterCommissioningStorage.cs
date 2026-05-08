using System.Text.Json;
using DotMatter.Core.Fabrics;
using DotMatter.Hosting.Storage;

namespace DotMatter.Hosting.Commissioning;

/// <summary>
/// Shared storage helpers used by hosted commissioning workflows.
/// </summary>
public static class MatterCommissioningStorage
{
    /// <summary>Directory name that stores per-device node metadata inside a shared fabric directory.</summary>
    public const string DeviceNodeInfoDirectoryName = "devices";

    private static readonly string[] _fabricIdentityFileNames =
    [
        "fabric.json",
        "rootCertificate.pem",
        "rootKeyPair.pem",
        "operationalCertificate.pem",
        "operationalKeyPair.pem",
    ];

    /// <summary>Copies controller fabric identity material into another fabric directory.</summary>
    public static void CopyFabricIdentity(string sourceDirectory, string destinationDirectory, bool applySensitiveFilePermissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);
        foreach (var fileName in _fabricIdentityFileNames)
        {
            var source = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Matter fabric identity file is missing: {source}", source);
            }

            var destination = Path.Combine(destinationDirectory, fileName);
            File.Copy(source, destination, overwrite: false);
            if (applySensitiveFilePermissions && FabricStorageSecurity.IsSensitiveFabricFile(fileName))
            {
                FabricStorageSecurity.TryApplyOwnerOnlyFilePermissions(destination);
            }
        }
    }

    /// <summary>Persists a commissioned node_info.json record under the given fabric directory.</summary>
    public static void WriteNodeInfo(string basePath, string fabricName, NodeInfoRecord nodeInfo)
    {
        var fabricDir = MatterFabricNames.GetFabricPath(basePath, fabricName);
        Directory.CreateDirectory(fabricDir);
        AtomicFilePersistence.WriteText(
            Path.Combine(fabricDir, "node_info.json"),
            JsonSerializer.Serialize(nodeInfo, HostingJsonIndentedContext.Default.NodeInfoRecord));
    }

    /// <summary>Persists a commissioned node_info.json record under the given fabric directory.</summary>
    public static async Task WriteNodeInfoAsync(
        string basePath,
        string fabricName,
        NodeInfoRecord nodeInfo,
        CancellationToken ct)
    {
        var fabricDir = MatterFabricNames.GetFabricPath(basePath, fabricName);
        Directory.CreateDirectory(fabricDir);
        await AtomicFilePersistence.WriteTextAsync(
            Path.Combine(fabricDir, "node_info.json"),
            JsonSerializer.Serialize(nodeInfo, HostingJsonIndentedContext.Default.NodeInfoRecord),
            ct);
    }

    /// <summary>Persists a commissioned device record under a shared fabric directory.</summary>
    public static async Task WriteDeviceNodeInfoAsync(
        string basePath,
        string fabricName,
        string deviceId,
        NodeInfoRecord nodeInfo,
        CancellationToken ct)
    {
        var deviceInfoPath = GetDeviceNodeInfoPath(basePath, fabricName, deviceId);
        Directory.CreateDirectory(Path.GetDirectoryName(deviceInfoPath)!);
        await AtomicFilePersistence.WriteTextAsync(
            deviceInfoPath,
            JsonSerializer.Serialize(nodeInfo, HostingJsonIndentedContext.Default.NodeInfoRecord),
            ct);
    }

    /// <summary>Returns the per-device metadata path inside a shared fabric directory.</summary>
    public static string GetDeviceNodeInfoPath(string basePath, string fabricName, string deviceId)
        => Path.Combine(
            MatterFabricNames.GetFabricPath(basePath, fabricName),
            DeviceNodeInfoDirectoryName,
            $"{MatterFabricNames.Normalize(deviceId)}.json");
}
