using DotMatter.Core;

namespace DotMatter.Controller;

public sealed partial class MatterControllerService
{
    /// <summary>Disconnects a device and removes its registry and fabric data locally.</summary>
    private bool DisconnectAndRemoveLocal(string id)
    {
        if (Sessions.TryRemove(id, out var rs))
        {
            rs.Disconnect();
        }

        Subscriptions.TryRemove(id, out _);
        LastSubscriptionReport.TryRemove(id, out _);
        LastPing.TryRemove(id, out _);
        DeviceListeners.TryRemove(id, out _);
        return Registry.Remove(id);
    }

    /// <summary>
    /// Deletes a device from the controller.
    /// By default this first removes the controller fabric from the node itself, then deletes local state.
    /// </summary>
    public async Task<DeviceOperationResult> DeleteDeviceAsync(string id, bool localOnly = false)
    {
        var device = Registry.Get(id);
        if (device is null)
        {
            return new(false, DeviceOperationFailure.NotFound, $"Device {id} not found");
        }

        if (localOnly)
        {
            return DisconnectAndRemoveLocal(id)
                ? DeviceOperationResult.Succeeded
                : new(false, DeviceOperationFailure.NotFound, $"Device {id} not found");
        }

        if (!TryGetSessionOwner(id, out var sessionOwner))
        {
            return new(false, DeviceOperationFailure.NotConnected,
                "Device is not connected. Use localOnly=true to remove only controller-side state.");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var operationalCredentials = await MatterAdministration.ReadOperationalCredentialsAsync(
                sessionOwner,
                endpointId: 0,
                cts.Token);

            var response = await MatterAdministration.RemoveFabricAsync(
                sessionOwner,
                operationalCredentials.CurrentFabricIndex,
                endpointId: 0,
                cts.Token);

            if (!response.Success)
            {
                Log.LogWarning(
                    "Device {Id}: RemoveFabric failed during delete (status 0x{Status:X2})",
                    id,
                    response.StatusCode);
                return new(false, DeviceOperationFailure.TransportError, $"Matter status 0x{response.StatusCode:X2}");
            }

            DisconnectAndRemoveLocal(id);
            PublishEvent(id, "admin", $"delete:fabric:{operationalCredentials.CurrentFabricIndex}");
            Log.LogInformation(
                "Device {Id}: removed fabric {FabricIndex} from node and deleted local state",
                id,
                operationalCredentials.CurrentFabricIndex);
            return DeviceOperationResult.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, "Delete timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Device {Id}: delete failed", id);
            return new(false, DeviceOperationFailure.TransportError, ex.Message);
        }
    }

    /// <summary>Reads commissioning state for a connected device through the supported Core admin helpers.</summary>
    internal async Task<DeviceCommissioningStateQueryResult> ReadCommissioningStateAsync(string id)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new(false, DeviceOperationFailure.NotConnected, null, "Device is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var state = await MatterAdministration.ReadCommissioningStateAsync(session, endpointId: 0, cts.Token);
            var device = Registry.Get(id);

            return new(true, DeviceOperationFailure.None, new DeviceCommissioningStateResponse(
                id,
                device?.Name,
                MapCommissioningState(state)));
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Commissioning state read timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>Reads operational credential state for a connected device through the supported Core admin helpers.</summary>
    internal async Task<DeviceOperationalCredentialsQueryResult> ReadOperationalCredentialsAsync(string id)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new(false, DeviceOperationFailure.NotConnected, null, "Device is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var state = await MatterAdministration.ReadOperationalCredentialsAsync(session, endpointId: 0, cts.Token);
            var device = Registry.Get(id);

            return new(true, DeviceOperationFailure.None, new DeviceOperationalCredentialsResponse(
                id,
                device?.Name,
                state.SupportedFabrics,
                state.CommissionedFabrics,
                state.CurrentFabricIndex,
                [.. state.Fabrics.Select(fabric => new DeviceFabricDescriptor(
                    Convert.ToHexString(fabric.RootPublicKey),
                    fabric.VendorID,
                    fabric.FabricID,
                    fabric.NodeID,
                    fabric.Label,
                    fabric.VIDVerificationStatement is null ? null : Convert.ToHexString(fabric.VIDVerificationStatement),
                    fabric.FabricIndex))],
                [.. state.Nocs.Select(noc => new DeviceNocDescriptor(
                    Convert.ToHexString(noc.NOC),
                    noc.ICAC is null ? null : Convert.ToHexString(noc.ICAC),
                    noc.VVSC is null ? null : Convert.ToHexString(noc.VVSC),
                    noc.FabricIndex))],
                [.. state.TrustedRootCertificates.Select(Convert.ToHexString)]));
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Operational credentials read timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>Opens a basic commissioning window on a connected device.</summary>
    public Task<DeviceOperationResult> OpenBasicCommissioningWindowAsync(string id, ushort commissioningTimeout)
        => ExecuteAdministrativeCommandAsync(
            id,
            "OpenBasicCommissioningWindow",
            ct => MatterAdministration.OpenBasicCommissioningWindowAsync(GetRequiredSessionOwner(id), commissioningTimeout, endpointId: 0, ct),
            $"commissioning window opened for {commissioningTimeout}s");

    /// <summary>Opens an enhanced commissioning window on a connected device.</summary>
    public Task<DeviceOperationResult> OpenCommissioningWindowAsync(string id, MatterEnhancedCommissioningWindowParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return ExecuteAdministrativeCommandAsync(
            id,
            "OpenCommissioningWindow",
            ct => MatterAdministration.OpenCommissioningWindowAsync(GetRequiredSessionOwner(id), parameters, endpointId: 0, ct),
            $"enhanced commissioning window opened for {parameters.CommissioningTimeout}s");
    }

    /// <summary>Revokes any open commissioning window on a connected device.</summary>
    public Task<DeviceOperationResult> RevokeCommissioningAsync(string id)
        => ExecuteAdministrativeCommandAsync(
            id,
            "RevokeCommissioning",
            ct => MatterAdministration.RevokeCommissioningAsync(GetRequiredSessionOwner(id), endpointId: 0, ct),
            "commissioning window revoked");

    /// <summary>Completes commissioning on a connected device.</summary>
    public Task<DeviceOperationResult> CompleteCommissioningAsync(string id)
        => ExecuteAdministrativeCommandAsync(
            id,
            "CommissioningComplete",
            ct => MatterAdministration.CompleteCommissioningAsync(GetRequiredSessionOwner(id), endpointId: 0, ct),
            "commissioning complete");

    /// <summary>Updates the current fabric label on a connected device.</summary>
    public Task<DeviceOperationResult> UpdateFabricLabelAsync(string id, string label)
        => ExecuteAdministrativeCommandAsync(
            id,
            "UpdateFabricLabel",
            ct => MatterAdministration.UpdateFabricLabelAsync(GetRequiredSessionOwner(id), label, endpointId: 0, ct),
            $"fabric label updated to '{label}'");

    /// <summary>Removes one fabric from a connected device.</summary>
    public Task<DeviceOperationResult> RemoveFabricAsync(string id, byte fabricIndex)
        => ExecuteAdministrativeCommandAsync(
            id,
            "RemoveFabric",
            ct => MatterAdministration.RemoveFabricAsync(GetRequiredSessionOwner(id), fabricIndex, endpointId: 0, ct),
            $"fabric {fabricIndex} removed");

    internal static DeviceCommissioningState MapCommissioningState(MatterCommissioningState state)
        => new(
            state.WindowStatus.ToString(),
            state.AdminFabricIndex,
            state.AdminVendorId,
            state.BasicCommissioningInfo is null
                ? null
                : new DeviceBasicCommissioningInfo(
                    state.BasicCommissioningInfo.FailSafeExpiryLengthSeconds,
                    state.BasicCommissioningInfo.MaxCumulativeFailsafeSeconds),
            state.TCAcceptedVersion,
            state.TCMinRequiredVersion,
            state.TCAcknowledgements,
            state.TCAcknowledgementsRequired,
            state.TCUpdateDeadline);
}
