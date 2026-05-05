using DotMatter.Core.Clusters;
using DotMatter.Core.Sessions;

namespace DotMatter.Core;

/// <summary>Descriptor-based device topology and identity information.</summary>
public sealed class MatterDeviceTopology(
    ushort dataModelRevision,
    string? vendorName,
    ushort vendorId,
    string? productName,
    ushort productId,
    string? nodeLabel,
    IReadOnlyList<MatterEndpointTopology> endpoints)
{
    /// <summary>Basic Information data-model revision.</summary>
    public ushort DataModelRevision { get; } = dataModelRevision;

    /// <summary>Vendor name reported by endpoint 0.</summary>
    public string? VendorName { get; } = vendorName;

    /// <summary>Vendor ID reported by endpoint 0.</summary>
    public ushort VendorId { get; } = vendorId;

    /// <summary>Product name reported by endpoint 0.</summary>
    public string? ProductName { get; } = productName;

    /// <summary>Product ID reported by endpoint 0.</summary>
    public ushort ProductId { get; } = productId;

    /// <summary>Node label reported by endpoint 0.</summary>
    public string? NodeLabel { get; } = nodeLabel;

    /// <summary>Descriptor topology for each discovered endpoint.</summary>
    public IReadOnlyList<MatterEndpointTopology> Endpoints { get; } = endpoints;
}

/// <summary>Descriptor information for one endpoint.</summary>
public sealed class MatterEndpointTopology(
    ushort endpointId,
    IReadOnlyList<MatterDeviceTypeDescriptor> deviceTypes,
    IReadOnlyList<uint> serverClusters,
    IReadOnlyList<uint> clientClusters,
    IReadOnlyList<ushort> partsList,
    string? uniqueId)
{
    /// <summary>Endpoint identifier.</summary>
    public ushort EndpointId { get; } = endpointId;

    /// <summary>Device types reported by the Descriptor cluster.</summary>
    public IReadOnlyList<MatterDeviceTypeDescriptor> DeviceTypes { get; } = deviceTypes;

    /// <summary>Server clusters hosted by this endpoint.</summary>
    public IReadOnlyList<uint> ServerClusters { get; } = serverClusters;

    /// <summary>Client clusters hosted by this endpoint.</summary>
    public IReadOnlyList<uint> ClientClusters { get; } = clientClusters;

    /// <summary>Child endpoints from the Descriptor PartsList attribute.</summary>
    public IReadOnlyList<ushort> PartsList { get; } = partsList;

    /// <summary>Optional endpoint-unique identifier.</summary>
    public string? UniqueId { get; } = uniqueId;
}

/// <summary>One device type entry from Descriptor.DeviceTypeList.</summary>
public sealed class MatterDeviceTypeDescriptor(uint deviceType, ushort revision)
{
    /// <summary>Matter device type identifier.</summary>
    public uint DeviceType { get; } = deviceType;

    /// <summary>Revision for the device type entry.</summary>
    public ushort Revision { get; } = revision;
}

/// <summary>Reusable Descriptor and Basic Information helpers for controller/client applications.</summary>
public static class MatterTopology
{
    /// <summary>Discover all reachable endpoint identifiers by following Descriptor PartsList links.</summary>
    public static async Task<IReadOnlyList<ushort>> DiscoverEndpointsAsync(ISession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var discovered = new HashSet<ushort> { 0 };
        var pending = new Queue<ushort>();
        pending.Enqueue(0);

        while (pending.Count > 0)
        {
            var endpointId = pending.Dequeue();
            var descriptor = new DescriptorCluster(session, endpointId);
            var children = await descriptor.ReadPartsListAsync(ct) ?? [];

            foreach (var child in children)
            {
                if (discovered.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }

        return discovered.OrderBy(static endpointId => endpointId).ToArray();
    }

    /// <summary>Discover all reachable endpoint identifiers through a resilient operational session.</summary>
    public static Task<IReadOnlyList<ushort>> DiscoverEndpointsAsync(ResilientSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => DiscoverEndpointsAsync(secureSession, ct), ct);
    }

    /// <summary>Read a topology snapshot using Descriptor and Basic Information clusters.</summary>
    public static async Task<MatterDeviceTopology> DescribeAsync(ISession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var basic = new BasicInformationCluster(session, endpointId: 0);
        var endpoints = await DiscoverEndpointsAsync(session, ct);
        var endpointTopologies = new List<MatterEndpointTopology>(endpoints.Count);

        foreach (var endpointId in endpoints)
        {
            var descriptor = new DescriptorCluster(session, endpointId);
            var deviceTypes = (await descriptor.ReadDeviceTypeListAsync(ct) ?? [])
                .Select(static deviceType => new MatterDeviceTypeDescriptor(deviceType.DeviceType, deviceType.Revision))
                .ToArray();

            endpointTopologies.Add(new MatterEndpointTopology(
                endpointId,
                deviceTypes,
                await descriptor.ReadServerListAsync(ct) ?? [],
                await descriptor.ReadClientListAsync(ct) ?? [],
                await descriptor.ReadPartsListAsync(ct) ?? [],
                await descriptor.ReadEndpointUniqueIDAsync(ct)));
        }

        return new MatterDeviceTopology(
            await basic.ReadDataModelRevisionAsync(ct),
            await basic.ReadVendorNameAsync(ct),
            await basic.ReadVendorIDAsync(ct),
            await basic.ReadProductNameAsync(ct),
            await basic.ReadProductIDAsync(ct),
            await basic.ReadNodeLabelAsync(ct),
            endpointTopologies);
    }

    /// <summary>Read a topology snapshot through a resilient operational session.</summary>
    public static Task<MatterDeviceTopology> DescribeAsync(ResilientSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => DescribeAsync(secureSession, ct), ct);
    }

    /// <summary>Find one endpoint topology entry by identifier.</summary>
    public static MatterEndpointTopology? FindEndpoint(MatterDeviceTopology topology, ushort endpointId)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return topology.Endpoints.FirstOrDefault(endpoint => endpoint.EndpointId == endpointId);
    }

    /// <summary>Return whether the given endpoint hosts the requested server cluster.</summary>
    public static bool EndpointSupportsServerCluster(MatterDeviceTopology topology, ushort endpointId, uint clusterId)
    {
        var endpoint = FindEndpoint(topology, endpointId);
        return endpoint?.ServerClusters.Contains(clusterId) == true;
    }
}
