using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Hosting.Devices;

namespace DotMatter.Controller.Matter;

internal static class DeviceCapabilitySnapshotBuilder
{
    internal static DeviceCapabilitySnapshot Build(DeviceInfo device, MatterDeviceTopology? topology)
    {
        ArgumentNullException.ThrowIfNull(device);

        var endpoints = topology is not null
            ? MapTopologyEndpoints(device, topology)
            : MapCachedEndpoints(device);
        var controllerDeviceType = ResolveControllerDeviceType(device, topology, endpoints);

        return new DeviceCapabilitySnapshot(
            SourceDeviceId: device.Id,
            SourceDeviceName: device.Name,
            IsOnline: device.IsOnline,
            TopologySource: topology is not null
                ? "live"
                : device.Endpoints is { Count: > 0 }
                    ? "cached"
                    : "unknown",
            ControllerDeviceType: controllerDeviceType,
            SupportsHueSaturation: device.SupportsHueSaturation,
            SupportsXy: device.SupportsXY,
            DataModelRevision: topology?.DataModelRevision,
            VendorName: topology?.VendorName ?? device.VendorName,
            VendorId: topology?.VendorId,
            VendorIdHex: topology is not null ? $"0x{topology.VendorId:X4}" : null,
            ProductName: topology?.ProductName ?? device.ProductName,
            ProductId: topology?.ProductId,
            ProductIdHex: topology is not null ? $"0x{topology.ProductId:X4}" : null,
            NodeLabel: topology?.NodeLabel,
            Endpoints: endpoints,
            SupportedOperations: BuildOperationSupport(device, endpoints));
    }

    private static DeviceCapabilityEndpoint[] MapTopologyEndpoints(DeviceInfo device, MatterDeviceTopology topology)
    {
        var cachedEndpoints = device.Endpoints?
            .ToDictionary(
                static entry => entry.Key,
                static entry => (IReadOnlyCollection<uint>)entry.Value)
            ?? new Dictionary<ushort, IReadOnlyCollection<uint>>();

        var mergedEndpoints = topology.Endpoints
            .OrderBy(static endpoint => endpoint.EndpointId)
            .Select(endpoint =>
            {
                cachedEndpoints.TryGetValue(endpoint.EndpointId, out var cachedServerClusters);

                return new DeviceCapabilityEndpoint(
                    Endpoint: endpoint.EndpointId,
                    EndpointHex: $"0x{endpoint.EndpointId:X4}",
                    DeviceTypes: [.. endpoint.DeviceTypes
                        .OrderBy(static deviceType => deviceType.DeviceType)
                        .Select(static deviceType => new DeviceCapabilityDeviceType(
                            deviceType.DeviceType,
                            $"0x{deviceType.DeviceType:X8}",
                            deviceType.Revision))],
                    ServerClusters: MapClusters(endpoint.ServerClusters.Concat(cachedServerClusters ?? [])),
                    ClientClusters: MapClusters(endpoint.ClientClusters),
                    PartsList: [.. endpoint.PartsList.OrderBy(static part => part)],
                    UniqueId: endpoint.UniqueId);
            })
            .ToList();

        foreach (var cachedOnlyEndpoint in cachedEndpoints.Keys
                     .Except(topology.Endpoints.Select(static endpoint => endpoint.EndpointId))
                     .OrderBy(static endpointId => endpointId))
        {
            mergedEndpoints.Add(new DeviceCapabilityEndpoint(
                Endpoint: cachedOnlyEndpoint,
                EndpointHex: $"0x{cachedOnlyEndpoint:X4}",
                DeviceTypes: [],
                ServerClusters: MapClusters(cachedEndpoints[cachedOnlyEndpoint]),
                ClientClusters: [],
                PartsList: [],
                UniqueId: null));
        }

        return [.. mergedEndpoints];
    }

    private static DeviceCapabilityEndpoint[] MapCachedEndpoints(DeviceInfo device)
        => device.Endpoints is not { Count: > 0 } endpoints
            ? []
            : [.. endpoints
                .OrderBy(static endpoint => endpoint.Key)
                .Select(static endpoint => new DeviceCapabilityEndpoint(
                    Endpoint: endpoint.Key,
                    EndpointHex: $"0x{endpoint.Key:X4}",
                    DeviceTypes: [],
                    ServerClusters: MapClusters(endpoint.Value),
                    ClientClusters: [],
                    PartsList: [],
                    UniqueId: null))];

    private static DeviceCapabilityCluster[] MapClusters(IEnumerable<uint> clusterIds)
        => [.. clusterIds
            .Distinct()
            .OrderBy(static clusterId => clusterId)
            .Select(static clusterId => new DeviceCapabilityCluster(
                ClusterId: clusterId,
                ClusterHex: $"0x{clusterId:X4}",
                ClusterName: GetClusterName(clusterId),
                SupportsEvents: ClusterEventRegistry.SupportsCluster(clusterId)))];

    private static DeviceOperationSupport BuildOperationSupport(
        DeviceInfo device,
        IReadOnlyList<DeviceCapabilityEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceOperationSupport(
            OnOff: HasApplicationCluster(endpoints, OnOffCluster.ClusterId),
            Level: HasApplicationCluster(endpoints, LevelControlCluster.ClusterId),
            ColorHueSaturation: HasApplicationCluster(endpoints, ColorControlCluster.ClusterId) && device.SupportsHueSaturation,
            ColorXy: HasApplicationCluster(endpoints, ColorControlCluster.ClusterId) && device.SupportsXY,
            NetworkCommissioning: HasCluster(endpoints, NetworkCommissioningCluster.ClusterId),
            Groups: HasApplicationCluster(endpoints, GroupsCluster.ClusterId),
            Scenes: HasApplicationCluster(endpoints, ScenesManagementCluster.ClusterId),
            GroupKeys: HasCluster(endpoints, GroupKeyManagementCluster.ClusterId),
            AccessControl: HasCluster(endpoints, AccessControlCluster.ClusterId),
            Binding: HasApplicationCluster(endpoints, BindingCluster.ClusterId),
            SwitchBinding: HasApplicationCluster(endpoints, SwitchCluster.ClusterId),
            MatterEvents: endpoints.SelectMany(static endpoint => endpoint.ServerClusters).Any(static cluster => cluster.SupportsEvents));
    }

    private static bool HasApplicationCluster(IReadOnlyList<DeviceCapabilityEndpoint> endpoints, uint clusterId)
        => endpoints.Any(endpoint => endpoint.Endpoint != 0 && endpoint.ServerClusters.Any(cluster => cluster.ClusterId == clusterId));

    private static bool HasCluster(IReadOnlyList<DeviceCapabilityEndpoint> endpoints, uint clusterId)
        => endpoints.Any(endpoint => endpoint.ServerClusters.Any(cluster => cluster.ClusterId == clusterId));

    private static string? ResolveControllerDeviceType(
        DeviceInfo device,
        MatterDeviceTopology? topology,
        IReadOnlyList<DeviceCapabilityEndpoint> endpoints)
    {
        if (topology is not null)
        {
            var mapped = topology.Endpoints
                .Where(static endpoint => endpoint.EndpointId != 0)
                .OrderBy(static endpoint => endpoint.EndpointId)
                .SelectMany(static endpoint => endpoint.DeviceTypes)
                .Select(static deviceType => MapDeviceTypeLabel(deviceType.DeviceType))
                .FirstOrDefault(static label => !string.IsNullOrWhiteSpace(label));

            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }
        }

        if (HasApplicationCluster(endpoints, SwitchCluster.ClusterId))
        {
            return "switch";
        }

        if (HasApplicationCluster(endpoints, ColorControlCluster.ClusterId))
        {
            return "color_light";
        }

        if (HasApplicationCluster(endpoints, LevelControlCluster.ClusterId))
        {
            return "dimmable_light";
        }

        if (HasApplicationCluster(endpoints, OnOffCluster.ClusterId))
        {
            return "on_off_light";
        }

        return string.IsNullOrWhiteSpace(device.DeviceType) ? null : device.DeviceType;
    }

    private static string? MapDeviceTypeLabel(uint deviceType)
        => MatterDeviceCatalog.GetDeviceTypeLabel(deviceType);

    private static string? GetClusterName(uint clusterId)
        => MatterDeviceCatalog.GetClusterName(clusterId);
}
