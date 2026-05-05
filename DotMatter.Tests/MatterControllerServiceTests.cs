using System.Text.Json;
using DotMatter.Controller;
using DotMatter.Controller.Models;
using DotMatter.Core.Clusters;
using DotMatter.Hosting;
using DotMatter.Hosting.Thread;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotMatter.Tests;

[TestFixture]
public class MatterControllerServiceTests
{
    [Test]
    public async Task DeleteDeviceAsync_DefaultModePreservesLocalStateWhenRemoteDeleteCannotRun()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();
        SeedRegisteredDevice(tempDirectory.Path, "seeded-device");

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        registry.LoadFromDisk();

        var service = CreateService(registry);

        var result = await service.DeleteDeviceAsync("seeded-device");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(DeviceOperationFailure.NotConnected));
            Assert.That(registry.Get("seeded-device"), Is.Not.Null);
            Assert.That(Directory.Exists(Path.Combine(tempDirectory.Path, "seeded-device")), Is.True);
        }
    }

    [Test]
    public async Task DeleteDeviceAsync_LocalOnlyRemovesRegistryAndFabricDirectory()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();
        SeedRegisteredDevice(tempDirectory.Path, "seeded-device");

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        registry.LoadFromDisk();

        var service = CreateService(registry);

        var result = await service.DeleteDeviceAsync("seeded-device", localOnly: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(registry.Get("seeded-device"), Is.Null);
            Assert.That(Directory.Exists(Path.Combine(tempDirectory.Path, "seeded-device")), Is.False);
        }
    }

    [Test]
    public async Task ReadCapabilitiesAsync_UsesCachedRegistrySnapshotByDefault()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();
        SeedRegisteredDevice(tempDirectory.Path, "cached-device");

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        registry.LoadFromDisk();
        registry.Update("cached-device", device =>
        {
            device.Endpoints = new Dictionary<ushort, List<uint>>
            {
                [0] = [DescriptorCluster.ClusterId, AccessControlCluster.ClusterId, NetworkCommissioningCluster.ClusterId],
                [1] = [OnOffCluster.ClusterId, LevelControlCluster.ClusterId, ColorControlCluster.ClusterId]
            };
            device.ColorCapabilities = ColorControlCluster.ColorCapabilitiesBitmap.XY;
        });

        var service = CreateService(registry);

        var result = await service.ReadCapabilitiesAsync("cached-device");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Response, Is.Not.Null);
            Assert.That(result.Response!.TopologySource, Is.EqualTo("cached"));
            Assert.That(result.Response.ControllerDeviceType, Is.EqualTo("color_light"));
            Assert.That(result.Response.SupportedOperations.OnOff, Is.True);
            Assert.That(result.Response.SupportedOperations.Level, Is.True);
            Assert.That(result.Response.SupportedOperations.ColorXy, Is.True);
            Assert.That(result.Response.SupportedOperations.AccessControl, Is.True);
            Assert.That(result.Response.SupportedOperations.NetworkCommissioning, Is.True);
        }
    }

    private static MatterControllerService CreateService(DeviceRegistry registry)
        => new(
            NullLogger<MatterControllerService>.Instance,
            registry,
            new MatterRuntimeStatus(),
            new FakeOtbrService(),
            Options.Create(new SessionRecoveryOptions()),
            Options.Create(new ControllerApiOptions()),
            Options.Create(new CommissioningOptions()));

    private static void SeedRegisteredDevice(string basePath, string fabricName)
    {
        var deviceDir = Path.Combine(basePath, fabricName);
        Directory.CreateDirectory(deviceDir);

        var nodeInfo = new NodeInfoRecord(
            NodeId: "1",
            ThreadIPv6: "fd00::1",
            FabricName: fabricName,
            DeviceName: fabricName,
            Transport: "Thread",
            Commissioned: DateTime.UtcNow);

        File.WriteAllText(
            Path.Combine(deviceDir, "node_info.json"),
            JsonSerializer.Serialize(nodeInfo, HostingJsonIndentedContext.Default.NodeInfoRecord));
    }

    private sealed class FakeOtbrService : IOtbrService
    {
        Task<string?> IOtbrService.GetActiveDatasetHexAsync(CancellationToken _)
            => Task.FromResult<string?>(null);

        Task<string?> IOtbrService.ResolveSrpServiceAddressAsync(string _, CancellationToken _1)
            => Task.FromResult<string?>(null);

        Task<string?> IOtbrService.DiscoverThreadIpAsync(ILogger _, CancellationToken _1)
            => Task.FromResult<string?>(null);

        Task IOtbrService.EnableSrpServerAsync(CancellationToken _) => Task.CompletedTask;

        Task<string?> IOtbrService.RunOtCtlAsync(string _, CancellationToken _1, bool _2)
            => Task.FromResult<string?>(null);
    }
}
