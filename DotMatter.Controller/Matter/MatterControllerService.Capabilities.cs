using DotMatter.Controller.Matter;
using DotMatter.Core;

namespace DotMatter.Controller;

public sealed partial class MatterControllerService
{
    internal sealed record DeviceCapabilitySnapshotQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceCapabilitySnapshot? Response,
        string? Error = null);

    internal async Task<DeviceCapabilitySnapshotQueryResult> ReadCapabilitiesAsync(string id, bool refresh = false)
    {
        var device = Registry.Get(id);
        if (device is null)
        {
            return new(false, DeviceOperationFailure.NotFound, null, $"Device {id} not found");
        }

        MatterDeviceTopology? topology = null;
        var shouldRefreshLive = refresh || device.Endpoints is not { Count: > 0 };
        if (shouldRefreshLive && TryGetSessionOwner(id, out var sessionOwner) && sessionOwner.Session is { } session)
        {
            try
            {
                using var endpointCts = new CancellationTokenSource(_apiOptions.CommandTimeout);
                await DiscoverDeviceEndpointsAsync(id, session, endpointCts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.LogWarning("Device {Id}: endpoint discovery timed out during capability read", id);
            }
            catch (Exception ex)
            {
                Log.LogWarning(ex, "Device {Id}: endpoint discovery failed during capability read", id);
            }

            try
            {
                using var stateCts = new CancellationTokenSource(_apiOptions.CommandTimeout);
                await ReadDeviceStateAsync(id, session, stateCts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.LogWarning("Device {Id}: state read timed out during capability read", id);
            }
            catch (Exception ex)
            {
                Log.LogWarning(ex, "Device {Id}: state read failed during capability read", id);
            }

            try
            {
                using var topologyCts = new CancellationTokenSource(_apiOptions.CommandTimeout);
                topology = await MatterTopology.DescribeAsync(session, topologyCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (device.Endpoints is null)
                {
                    return new(false, DeviceOperationFailure.Timeout, null, "Device capability discovery timed out");
                }

                Log.LogWarning("Device {Id}: capability topology discovery timed out; falling back to cached endpoint map", id);
            }
            catch (Exception ex)
            {
                if (device.Endpoints is null)
                {
                    return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
                }

                Log.LogWarning(ex, "Device {Id}: capability topology discovery failed; falling back to cached endpoint map", id);
            }
        }

        device = Registry.Get(id) ?? device;
        var snapshot = DeviceCapabilitySnapshotBuilder.Build(device, topology);
        Registry.Update(id, current =>
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ControllerDeviceType))
            {
                current.DeviceType = snapshot.ControllerDeviceType!;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.VendorName))
            {
                current.VendorName = snapshot.VendorName;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ProductName))
            {
                current.ProductName = snapshot.ProductName;
            }
        });
        Registry.PersistDeviceCache(id);

        return new(
            true,
            DeviceOperationFailure.None,
            snapshot);
    }
}
