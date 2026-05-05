using Tmds.DBus.Protocol;

namespace DotMatter.Core.LinuxBle;

/// <summary>Scans BlueZ for actively advertising Matter BLE commissionable devices.</summary>
public sealed class MatterBleAdvertisementScanner(LinuxBleOptions? options = null)
{
    private readonly LinuxBleOptions _options = options ?? new LinuxBleOptions();

    /// <summary>Browse current Matter BLE advertisers during the given timeout window.</summary>
    public async Task<IReadOnlyList<MatterBleAdvertisement>> ScanAsync(
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        using var connection = new DBusConnection(DBusAddress.System!);
        await connection.ConnectAsync();

        var objectManager = new ObjectManager(connection, "org.bluez", "/");
        var adapterPath = await GetAdapterPathAsync(objectManager);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            throw new MatterTransportException("No Bluetooth adapter found");
        }

        var adapter = new Adapter1(connection, "org.bluez", adapterPath);
        await ClearStaleMatterDevicesAsync(adapter, objectManager, adapterPath);

        try
        {
            await adapter.SetDiscoveryFilterAsync(new Dictionary<string, VariantValue>
            {
                { "Transport", VariantValue.String("le") }
            });
        }
        catch
        {
        }

        await adapter.StartDiscoveryAsync();

        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scanCts.CancelAfter(timeout ?? _options.ScanTimeout);

        var advertisements = new Dictionary<string, MatterBleAdvertisement>(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), scanCts.Token);
                foreach (var advertisement in await ReadAdvertisementsAsync(objectManager, adapterPath))
                {
                    if (!advertisements.TryGetValue(advertisement.BluetoothAddress, out var existing)
                        || advertisement.Rssi.GetValueOrDefault(short.MinValue) > existing.Rssi.GetValueOrDefault(short.MinValue))
                    {
                        advertisements[advertisement.BluetoothAddress] = advertisement;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                await adapter.StopDiscoveryAsync();
            }
            catch
            {
            }
        }

        return advertisements.Values
            .OrderByDescending(static advertisement => advertisement.Rssi.GetValueOrDefault(short.MinValue))
            .ThenBy(static advertisement => advertisement.Name ?? advertisement.BluetoothAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<string?> GetAdapterPathAsync(ObjectManager objectManager)
    {
        var objects = await objectManager.GetManagedObjectsAsync();
        foreach (var (path, interfaces) in objects)
        {
            if (interfaces.ContainsKey("org.bluez.Adapter1"))
            {
                return path;
            }
        }

        return null;
    }

    private static async Task ClearStaleMatterDevicesAsync(
        Adapter1 adapter,
        ObjectManager objectManager,
        string adapterPath)
    {
        var objects = await objectManager.GetManagedObjectsAsync();
        foreach (var (path, interfaces) in objects)
        {
            if (!interfaces.TryGetValue("org.bluez.Device1", out var deviceProps)
                || !((string)path).StartsWith(adapterPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (deviceProps.TryGetValue("Connected", out var connected) && connected.GetBool())
            {
                continue;
            }

            if (deviceProps.ContainsKey("RSSI") || !MatterBleAdvertisementParser.HasMatterServiceData(deviceProps))
            {
                continue;
            }

            try
            {
                await adapter.RemoveDeviceAsync(path);
            }
            catch
            {
            }
        }
    }

    private static async Task<IReadOnlyList<MatterBleAdvertisement>> ReadAdvertisementsAsync(
        ObjectManager objectManager,
        string adapterPath)
    {
        var objects = await objectManager.GetManagedObjectsAsync();
        return objects
            .Where(entry => entry.Value.TryGetValue("org.bluez.Device1", out _)
                && ((string)entry.Key).StartsWith(adapterPath, StringComparison.Ordinal))
            .Select(entry => entry.Value["org.bluez.Device1"])
            .Where(static deviceProps => deviceProps.ContainsKey("RSSI"))
            .Select(deviceProps => MatterBleAdvertisementParser.TryParse(deviceProps, out var advertisement)
                ? advertisement
                : null)
            .Where(static advertisement => advertisement is not null)
            .Cast<MatterBleAdvertisement>()
            .ToArray();
    }
}
