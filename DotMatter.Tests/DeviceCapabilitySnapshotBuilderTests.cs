using DotMatter.Core;
using DotMatter.Core.Clusters;

namespace DotMatter.Tests;

[TestFixture]
public class DeviceCapabilitySnapshotBuilderTests
{
    [Test]
    public void Build_SwitchTopology_HidesColorAndLevelOperations()
    {
        var device = new DeviceInfo
        {
            Id = "switch-1",
            Name = "Switch 1",
            IsOnline = true,
            DeviceType = "switch",
        };

        var topology = new MatterDeviceTopology(
            dataModelRevision: 17,
            vendorName: "Vendor",
            vendorId: 0x1234,
            productName: "Switch",
            productId: 0x5678,
            nodeLabel: "Switch Node",
            endpoints:
            [
                new MatterEndpointTopology(
                    endpointId: 0,
                    deviceTypes: [new MatterDeviceTypeDescriptor(0x00000016, 1)],
                    serverClusters: [DescriptorCluster.ClusterId, AccessControlCluster.ClusterId, OperationalCredentialsCluster.ClusterId],
                    clientClusters: [],
                    partsList: [1],
                    uniqueId: null),
                new MatterEndpointTopology(
                    endpointId: 1,
                    deviceTypes: [new MatterDeviceTypeDescriptor(0x0000000F, 1)],
                    serverClusters: [SwitchCluster.ClusterId, BindingCluster.ClusterId],
                    clientClusters: [],
                    partsList: [],
                    uniqueId: "switch-ep")
            ]);

        var snapshot = DeviceCapabilitySnapshotBuilder.Build(device, topology);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.TopologySource, Is.EqualTo("live"));
            Assert.That(snapshot.ControllerDeviceType, Is.EqualTo("switch"));
            Assert.That(snapshot.Endpoints.Select(endpoint => endpoint.Endpoint), Is.EqualTo(new ushort[] { 0, 1 }));
            Assert.That(snapshot.SupportedOperations.SwitchBinding, Is.True);
            Assert.That(snapshot.SupportedOperations.Binding, Is.True);
            Assert.That(snapshot.SupportedOperations.AccessControl, Is.True);
            Assert.That(snapshot.SupportedOperations.Level, Is.False);
            Assert.That(snapshot.SupportedOperations.ColorHueSaturation, Is.False);
            Assert.That(snapshot.SupportedOperations.ColorXy, Is.False);
            Assert.That(snapshot.SupportedOperations.MatterEvents, Is.True);
        }
    }

    [Test]
    public void Build_XyOnlyColorDevice_ExposesXyButNotHueSaturation()
    {
        var device = new DeviceInfo
        {
            Id = "color-1",
            Name = "Color 1",
            IsOnline = true,
            DeviceType = "color_light",
            ColorCapabilities = ColorControlCluster.ColorCapabilitiesBitmap.XY,
        };

        var topology = new MatterDeviceTopology(
            dataModelRevision: 17,
            vendorName: "Vendor",
            vendorId: 0x1234,
            productName: "Color Lamp",
            productId: 0x5678,
            nodeLabel: "Color Node",
            endpoints:
            [
                new MatterEndpointTopology(
                    endpointId: 0,
                    deviceTypes: [new MatterDeviceTypeDescriptor(0x00000100, 1)],
                    serverClusters: [DescriptorCluster.ClusterId],
                    clientClusters: [],
                    partsList: [1],
                    uniqueId: null),
                new MatterEndpointTopology(
                    endpointId: 1,
                    deviceTypes: [new MatterDeviceTypeDescriptor(0x0000010D, 1)],
                    serverClusters: [OnOffCluster.ClusterId, LevelControlCluster.ClusterId, ColorControlCluster.ClusterId],
                    clientClusters: [],
                    partsList: [],
                    uniqueId: "lamp-ep")
            ]);

        var snapshot = DeviceCapabilitySnapshotBuilder.Build(device, topology);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.ControllerDeviceType, Is.EqualTo("color_light"));
            Assert.That(snapshot.SupportedOperations.OnOff, Is.True);
            Assert.That(snapshot.SupportedOperations.Level, Is.True);
            Assert.That(snapshot.SupportedOperations.ColorHueSaturation, Is.False);
            Assert.That(snapshot.SupportedOperations.ColorXy, Is.True);
        }
    }

    [Test]
    public void Build_RootOnlyTopology_MergesCachedApplicationEndpoints()
    {
        var device = new DeviceInfo
        {
            Id = "switch-root-only",
            Name = "Switch Root Only",
            IsOnline = true,
            DeviceType = "on_off_light",
            Endpoints = new Dictionary<ushort, List<uint>>
            {
                [0] = [DescriptorCluster.ClusterId, AccessControlCluster.ClusterId, OperationalCredentialsCluster.ClusterId],
                [1] = [SwitchCluster.ClusterId, BindingCluster.ClusterId]
            }
        };

        var topology = new MatterDeviceTopology(
            dataModelRevision: 19,
            vendorName: null,
            vendorId: 0,
            productName: null,
            productId: 0,
            nodeLabel: null,
            endpoints:
            [
                new MatterEndpointTopology(
                    endpointId: 0,
                    deviceTypes:
                    [
                        new MatterDeviceTypeDescriptor(0x00000012, 1),
                        new MatterDeviceTypeDescriptor(0x00000016, 1)
                    ],
                    serverClusters: [],
                    clientClusters: [],
                    partsList: [],
                    uniqueId: null)
            ]);

        var snapshot = DeviceCapabilitySnapshotBuilder.Build(device, topology);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.TopologySource, Is.EqualTo("live"));
            Assert.That(snapshot.ControllerDeviceType, Is.EqualTo("switch"));
            Assert.That(snapshot.Endpoints.Select(endpoint => endpoint.Endpoint), Is.EqualTo(new ushort[] { 0, 1 }));
            Assert.That(snapshot.SupportedOperations.SwitchBinding, Is.True);
            Assert.That(snapshot.SupportedOperations.Binding, Is.True);
            Assert.That(snapshot.SupportedOperations.AccessControl, Is.True);
        }
    }
}
