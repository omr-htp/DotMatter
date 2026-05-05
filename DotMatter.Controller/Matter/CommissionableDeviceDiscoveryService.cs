using System.Globalization;
using DotMatter.Core.Discovery;
using DotMatter.Core.LinuxBle;

namespace DotMatter.Controller.Matter;

/// <summary>Controller-facing service for browsing and resolving currently advertising commissionable Matter nodes.</summary>
public interface ICommissionableDeviceDiscoveryService
{
    /// <summary>Browses current commissionable-device advertisements and returns the matched typed results.</summary>
    Task<CommissionableDeviceBrowseResponse> BrowseAsync(CommissionableDeviceDiscoveryRequest request, CancellationToken ct);

    /// <summary>Resolves the first currently advertising commissionable device that matches the requested discriminator.</summary>
    Task<CommissionableDeviceResponse?> ResolveAsync(CommissionableDeviceResolveRequest request, CancellationToken ct);
}

internal sealed class CommissionableDeviceDiscoveryService : ICommissionableDeviceDiscoveryService
{
    public async Task<CommissionableDeviceBrowseResponse> BrowseAsync(CommissionableDeviceDiscoveryRequest request, CancellationToken ct)
    {
        var discovered = new List<CommissionableDeviceResponse>();

        if (request.Transport is CommissionableDiscoveryTransport.All or CommissionableDiscoveryTransport.Mdns)
        {
            using var discovery = new CommissionableDiscovery();
            var nodes = await discovery.BrowseAsync(request.Timeout, ct);
            discovered.AddRange(nodes.Select(MapMdnsNode));
        }

        if (request.Transport is CommissionableDiscoveryTransport.All or CommissionableDiscoveryTransport.Ble)
        {
            var scanner = new MatterBleAdvertisementScanner();
            var advertisements = await scanner.ScanAsync(request.Timeout, ct);
            discovered.AddRange(advertisements.Select(MapBleAdvertisement));
        }

        var matched = discovered
            .Where(device => MatchesFilters(device, request))
            .OrderBy(device => device.DeviceName ?? device.InstanceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Transport, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.InstanceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var returned = request.Limit.HasValue
            ? [.. matched.Take(request.Limit.Value)]
            : matched;

        return new CommissionableDeviceBrowseResponse(
            BrowseWindowMs: (int)Math.Round(request.Timeout.TotalMilliseconds),
            TotalDiscovered: discovered.Count,
            MatchedCount: matched.Length,
            ReturnedCount: returned.Length,
            Devices: [.. returned.Select(device => request.IncludeTxtRecords
                ? device
                : device with
                {
                    TxtRecords = null,
                    ServiceDataHex = null
                })]);
    }

    public async Task<CommissionableDeviceResponse?> ResolveAsync(CommissionableDeviceResolveRequest request, CancellationToken ct)
    {
        var browse = await BrowseAsync(new CommissionableDeviceDiscoveryRequest(
            Timeout: request.Timeout,
            Transport: request.Transport,
            Discriminator: request.Discriminator,
            Limit: 1,
            IncludeTxtRecords: request.IncludeTxtRecords), ct);
        return browse.Devices.FirstOrDefault();
    }

    private static bool MatchesFilters(CommissionableDeviceResponse device, CommissionableDeviceDiscoveryRequest request)
    {
        if (request.Discriminator.HasValue && !MatchesDiscriminator(device, request.Discriminator.Value))
        {
            return false;
        }

        if (request.VendorId.HasValue && device.VendorId != request.VendorId.Value)
        {
            return false;
        }

        if (request.ProductId.HasValue && device.ProductId != request.ProductId.Value)
        {
            return false;
        }

        if (request.DeviceType.HasValue && device.DeviceType != request.DeviceType.Value)
        {
            return false;
        }

        if (request.CommissioningMode.HasValue && device.CommissioningMode != request.CommissioningMode.Value)
        {
            return false;
        }

        if (!Contains(device.DeviceName, request.DeviceNameContains))
        {
            return false;
        }

        if (!Contains(device.InstanceName, request.InstanceNameContains)
            && !Contains(device.BluetoothAddress, request.InstanceNameContains))
        {
            return false;
        }

        return Contains(device.RotatingIdentifier, request.RotatingIdentifierContains);
    }

    private static CommissionableDeviceResponse MapMdnsNode(CommissionableNode node)
    {
        var addresses = node.Addresses
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var shortDiscriminator = node.LongDiscriminator.HasValue
            ? (ushort?)(node.LongDiscriminator.Value >> 8)
            : null;

        return new CommissionableDeviceResponse(
            Transport: "mdns",
            InstanceName: node.InstanceName,
            FullyQualifiedName: node.FullyQualifiedName,
            Port: node.Port,
            Addresses: addresses,
            PreferredAddress: addresses.FirstOrDefault(),
            BluetoothAddress: null,
            Rssi: null,
            LongDiscriminator: node.LongDiscriminator,
            LongDiscriminatorHex: FormatHex(node.LongDiscriminator, 3),
            ShortDiscriminator: shortDiscriminator,
            ShortDiscriminatorHex: FormatHex(shortDiscriminator, 1),
            VendorId: node.VendorId,
            VendorIdHex: FormatHex(node.VendorId, 4),
            ProductId: node.ProductId,
            ProductIdHex: FormatHex(node.ProductId, 4),
            DeviceType: node.DeviceType,
            DeviceTypeHex: FormatHex(node.DeviceType, 8),
            DeviceName: node.DeviceName,
            CommissioningMode: node.CommissioningMode,
            PairingHint: node.PairingHint,
            PairingInstruction: node.PairingInstruction,
            RotatingIdentifier: node.RotatingIdentifier,
            AdvertisementVersion: null,
            ServiceDataHex: null,
            TxtRecords: node.TxtRecords.Count == 0
                ? null
                : new Dictionary<string, string>(node.TxtRecords, StringComparer.OrdinalIgnoreCase));
    }

    private static CommissionableDeviceResponse MapBleAdvertisement(MatterBleAdvertisement advertisement)
        => new(
            Transport: "ble",
            InstanceName: advertisement.BluetoothAddress,
            FullyQualifiedName: advertisement.BluetoothAddress,
            Port: 0,
            Addresses: [],
            PreferredAddress: advertisement.BluetoothAddress,
            BluetoothAddress: advertisement.BluetoothAddress,
            Rssi: advertisement.Rssi,
            LongDiscriminator: advertisement.LongDiscriminator,
            LongDiscriminatorHex: FormatHex(advertisement.LongDiscriminator, 3),
            ShortDiscriminator: advertisement.ShortDiscriminator,
            ShortDiscriminatorHex: FormatHex(advertisement.ShortDiscriminator, 1),
            VendorId: advertisement.VendorId,
            VendorIdHex: FormatHex(advertisement.VendorId, 4),
            ProductId: advertisement.ProductId,
            ProductIdHex: FormatHex(advertisement.ProductId, 4),
            DeviceType: null,
            DeviceTypeHex: null,
            DeviceName: advertisement.Name,
            CommissioningMode: null,
            PairingHint: null,
            PairingInstruction: null,
            RotatingIdentifier: null,
            AdvertisementVersion: advertisement.AdvertisementVersion,
            ServiceDataHex: advertisement.ServiceDataHex,
            TxtRecords: null);

    private static bool Contains(string? value, string? expected)
        => string.IsNullOrWhiteSpace(expected)
            || (!string.IsNullOrWhiteSpace(value)
                && value.Contains(expected, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesDiscriminator(CommissionableDeviceResponse device, ushort discriminator)
    {
        if (!device.LongDiscriminator.HasValue)
        {
            return false;
        }

        return discriminator <= 0x000F
            ? (device.LongDiscriminator.Value >> 8) == discriminator
            : device.LongDiscriminator.Value == discriminator;
    }

    private static string? FormatHex(ushort? value, int width)
        => value.HasValue
            ? $"0x{value.Value.ToString($"X{width}", CultureInfo.InvariantCulture)}"
            : null;

    private static string? FormatHex(uint? value, int width)
        => value.HasValue
            ? $"0x{value.Value.ToString($"X{width}", CultureInfo.InvariantCulture)}"
            : null;
}
