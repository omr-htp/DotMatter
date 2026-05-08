using System.Text;
using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;

namespace DotMatter.Controller;

public sealed partial class MatterControllerService
{
    /// <summary>Reads Network Commissioning state for a connected device through the supported Core admin helpers.</summary>
    internal async Task<DeviceNetworkCommissioningQueryResult> ReadNetworkCommissioningStateAsync(string id)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new(false, DeviceOperationFailure.NotConnected, null, "Device is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var state = await MatterAdministration.ReadNetworkCommissioningStateAsync(session, endpointId: 0, cts.Token);
            var device = Registry.Get(id);

            return new(true, DeviceOperationFailure.None, new DeviceNetworkCommissioningStateResponse(
                id,
                device?.Name,
                MapNetworkCommissioningState(state)));
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Network commissioning read timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceNetworkCommissioningScanResult> ScanNetworksAsync(string id, byte[]? ssid, ulong? breadcrumb)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new(false, DeviceOperationFailure.NotConnected, null, "Device is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var result = await MatterAdministration.ScanNetworksAsync(session, ssid, breadcrumb, endpointId: 0, cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceNetworkCommissioningScanResponse(
                id,
                device?.Name,
                result.InvokeSucceeded,
                result.Accepted,
                DescribeInteractionModelStatus(result.InvokeStatusCode),
                ToStatusHex(result.InvokeStatusCode),
                result.NetworkingStatus?.ToString(),
                result.DebugText,
                [.. result.WiFiScanResults.Select(MapWiFiScanResult)],
                [.. result.ThreadScanResults.Select(MapThreadScanResult)],
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "network", "scan");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "Network scan failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Network scan timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceNetworkCommissioningCommandResult> AddOrUpdateWiFiNetworkAsync(
        string id,
        byte[] ssid,
        byte[] credentials,
        ulong? breadcrumb,
        byte[]? networkIdentity,
        byte[]? clientIdentifier,
        byte[]? possessionNonce)
    {
        return await ExecuteNetworkConfigCommandAsync(
            id,
            "AddOrUpdateWiFiNetwork",
            ct => MatterAdministration.AddOrUpdateWiFiNetworkAsync(
                GetRequiredSessionOwner(id),
                ssid,
                credentials,
                breadcrumb,
                networkIdentity,
                clientIdentifier,
                possessionNonce,
                endpointId: 0,
                ct),
            successEvent: $"wifi:{Convert.ToHexString(ssid)}");
    }

    internal async Task<DeviceNetworkCommissioningCommandResult> AddOrUpdateThreadNetworkAsync(
        string id,
        byte[] operationalDataset,
        ulong? breadcrumb)
    {
        return await ExecuteNetworkConfigCommandAsync(
            id,
            "AddOrUpdateThreadNetwork",
            ct => MatterAdministration.AddOrUpdateThreadNetworkAsync(
                GetRequiredSessionOwner(id),
                operationalDataset,
                breadcrumb,
                endpointId: 0,
                ct),
            successEvent: "thread-updated");
    }

    internal async Task<DeviceNetworkCommissioningConnectResult> ConnectNetworkAsync(
        string id,
        byte[] networkId,
        ulong? breadcrumb)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new(false, DeviceOperationFailure.NotConnected, null, "Device is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var result = await MatterAdministration.ConnectNetworkAsync(session, networkId, breadcrumb, endpointId: 0, cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceNetworkCommissioningConnectResponse(
                id,
                device?.Name,
                result.InvokeSucceeded,
                result.Accepted,
                DescribeInteractionModelStatus(result.InvokeStatusCode),
                ToStatusHex(result.InvokeStatusCode),
                result.NetworkingStatus?.ToString(),
                result.DebugText,
                result.ErrorValue,
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "network", $"connect:{Convert.ToHexString(networkId)}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "Network connect failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Network connect timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceNetworkCommissioningCommandResult> RemoveNetworkAsync(string id, byte[] networkId, ulong? breadcrumb)
    {
        return await ExecuteNetworkConfigCommandAsync(
            id,
            "RemoveNetwork",
            ct => MatterAdministration.RemoveNetworkAsync(GetRequiredSessionOwner(id), networkId, breadcrumb, endpointId: 0, ct),
            successEvent: $"remove:{Convert.ToHexString(networkId)}");
    }

    internal async Task<DeviceNetworkCommissioningCommandResult> ReorderNetworkAsync(string id, byte[] networkId, byte networkIndex, ulong? breadcrumb)
    {
        return await ExecuteNetworkConfigCommandAsync(
            id,
            "ReorderNetwork",
            ct => MatterAdministration.ReorderNetworkAsync(GetRequiredSessionOwner(id), networkId, networkIndex, breadcrumb, endpointId: 0, ct),
            successEvent: $"reorder:{Convert.ToHexString(networkId)}:{networkIndex}");
    }

    internal async Task<DeviceNetworkInterfaceWriteResult> WriteNetworkInterfaceEnabledAsync(string id, bool interfaceEnabled)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new(false, DeviceOperationFailure.NotConnected, null, "Device is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var write = await MatterAdministration.WriteNetworkInterfaceEnabledAsync(session, interfaceEnabled, endpointId: 0, ct: cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceNetworkInterfaceWriteResponse(
                id,
                device?.Name,
                write.Success,
                write.StatusCode is { } statusCode ? $"0x{statusCode:X2}" : null,
                [.. write.AttributeStatuses.Select(static status => new DeviceAttributeWriteStatusResponse(
                    status.AttributeId,
                    $"0x{status.AttributeId:X4}",
                    status.StatusCode,
                    $"0x{status.StatusCode:X2}",
                    status.EndpointId,
                    status.ClusterId,
                    status.ClusterId is { } clusterId ? $"0x{clusterId:X4}" : null,
                    status.ClusterStatusCode,
                    status.ClusterStatusCode is { } clusterStatusCode ? $"0x{clusterStatusCode:X2}" : null))],
                write.Success ? null : FormatWriteResponse(write));

            if (write.Success)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "network", $"interface:{(interfaceEnabled ? "enabled" : "disabled")}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, FormatWriteResponse(write));
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Network interface write timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    private async Task<DeviceNetworkCommissioningCommandResult> ExecuteNetworkConfigCommandAsync(
        string id,
        string operationName,
        Func<CancellationToken, Task<MatterNetworkConfigCommandResult>> executeAsync,
        string successEvent)
    {
        if (!TryGetSessionOwner(id, out _))
        {
            return new(false, DeviceOperationFailure.NotConnected, null, "Device is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var result = await executeAsync(cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceNetworkCommissioningCommandResponse(
                id,
                device?.Name,
                result.InvokeSucceeded,
                result.Accepted,
                DescribeInteractionModelStatus(result.InvokeStatusCode),
                ToStatusHex(result.InvokeStatusCode),
                result.NetworkingStatus?.ToString(),
                result.DebugText,
                result.NetworkIndex,
                ToHex(result.ClientIdentity),
                ToHex(result.PossessionSignature),
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "network", successEvent);
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? $"{operationName} failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, $"{operationName} timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal static DeviceNetworkCommissioningState MapNetworkCommissioningState(MatterNetworkCommissioningState state)
        => new(
            ExpandNetworkFeatures(state.Features),
            state.MaxNetworks,
            state.ScanMaxTimeSeconds,
            state.ConnectMaxTimeSeconds,
            state.InterfaceEnabled,
            [.. state.SupportedWiFiBands.Select(static band => band.ToString())],
            ExpandThreadCapabilities(state.SupportedThreadFeatures),
            state.ThreadVersion,
            state.LastNetworkingStatus?.ToString(),
            ToHex(state.LastNetworkId),
            TryDecodeDisplayText(state.LastNetworkId),
            state.LastConnectErrorValue,
            [.. state.Networks.Select(MapNetworkCommissioningNetwork)]);

    private static DeviceNetworkCommissioningNetwork MapNetworkCommissioningNetwork(MatterNetworkCommissioningNetwork network)
        => new(
            Convert.ToHexString(network.NetworkId),
            TryDecodeDisplayText(network.NetworkId),
            network.Connected,
            ToHex(network.NetworkIdentifier),
            TryDecodeDisplayText(network.NetworkIdentifier),
            ToHex(network.ClientIdentifier));

    private static DeviceNetworkCommissioningWiFiScanResult MapWiFiScanResult(MatterWiFiScanResult result)
        => new(
            Convert.ToHexString(result.Ssid),
            TryDecodeDisplayText(result.Ssid),
            Convert.ToHexString(result.Bssid),
            result.Channel,
            result.WiFiBand.ToString(),
            result.Rssi,
            ExpandWiFiSecurity(result.Security));

    private static DeviceNetworkCommissioningThreadScanResult MapThreadScanResult(MatterThreadScanResult result)
        => new(
            result.PanId,
            $"{result.ExtendedPanId:X16}",
            result.NetworkName,
            result.Channel,
            result.Version,
            Convert.ToHexString(result.ExtendedAddress),
            result.Rssi,
            result.Lqi);

    private static string[] ExpandNetworkFeatures(NetworkCommissioningCluster.Feature? features)
    {
        if (!features.HasValue || features.Value == 0)
        {
            return [];
        }

        return [.. Enum.GetValues<NetworkCommissioningCluster.Feature>()
            .Where(flag => flag != 0 && features.Value.HasFlag(flag))
            .Select(static flag => flag.ToString())];
    }

    private static string[] ExpandThreadCapabilities(NetworkCommissioningCluster.ThreadCapabilitiesBitmap? capabilities)
    {
        if (!capabilities.HasValue || capabilities.Value == 0)
        {
            return [];
        }

        return [.. Enum.GetValues<NetworkCommissioningCluster.ThreadCapabilitiesBitmap>()
            .Where(flag => flag != 0 && capabilities.Value.HasFlag(flag))
            .Select(static flag => flag.ToString())];
    }

    private static string[] ExpandWiFiSecurity(NetworkCommissioningCluster.WiFiSecurityBitmap security)
    {
        if (security == 0)
        {
            return [];
        }

        return [.. Enum.GetValues<NetworkCommissioningCluster.WiFiSecurityBitmap>()
            .Where(flag => flag != 0 && security.HasFlag(flag))
            .Select(static flag => flag.ToString())];
    }

    private static string? ToHex(byte[]? data)
        => data is null ? null : Convert.ToHexString(data);

    private static string? ToStatusHex(byte? statusCode)
        => statusCode is { } value ? $"0x{value:X2}" : null;

    private static string? DescribeInteractionModelStatus(byte? statusCode)
        => statusCode is not { } value
            ? null
            : Enum.IsDefined(typeof(MatterStatusCode), value)
                ? $"{(MatterStatusCode)value} (0x{value:X2})"
                : $"0x{value:X2}";

    private static string? TryDecodeDisplayText(byte[]? data)
    {
        if (data is null || data.Length == 0)
        {
            return null;
        }

        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
            return text.All(static c => !char.IsControl(c)) ? text : null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
