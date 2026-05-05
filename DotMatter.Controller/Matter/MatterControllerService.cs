using System.Collections.Concurrent;
using System.Threading.Channels;
using DotMatter.Controller.Configuration;
using DotMatter.Controller.Diagnostics;
using DotMatter.Controller.Matter;
using DotMatter.Core;
using DotMatter.Core.InteractionModel;
using DotMatter.Hosting.Devices;
using DotMatter.Hosting.Runtime;
using DotMatter.Hosting.Thread;
using Microsoft.Extensions.Options;

namespace DotMatter.Controller;

/// <summary>
/// Commands supported by the On/Off device API.
/// </summary>
public enum DeviceCommand
{
    /// <summary>Turn on.</summary>
    On,
    /// <summary>Turn off.</summary>
    Off,
    /// <summary>Toggle on/off state.</summary>
    Toggle,
}

/// <summary>
/// Represents an active server-sent events subscription.
/// </summary>
public sealed class ControllerEventSubscription(ChannelReader<string> reader, Func<ValueTask> disposeAsync) : IAsyncDisposable
{
    private readonly Func<ValueTask> _disposeAsync = disposeAsync;

    /// <summary>Gets the stream reader for serialized event payloads.</summary>
    public ChannelReader<string> Reader { get; } = reader;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _disposeAsync();
}

/// <summary>
/// Controller-specific Matter device host. Extends MatterDeviceHost with
/// REST API command methods and SSE event broadcasting.
/// </summary>
public sealed partial class MatterControllerService(
    ILogger<MatterControllerService> log,
    DeviceRegistry registry,
    MatterRuntimeStatus runtimeStatus,
    IOtbrService otbrService,
    IOptions<SessionRecoveryOptions> recoveryOptions,
    IOptions<ControllerApiOptions> apiOptions,
    IOptions<CommissioningOptions> commissioningOptions) : MatterDeviceHost(log, registry, runtimeStatus, otbrService, recoveryOptions.Value)
{
    private readonly ConcurrentDictionary<int, Channel<string>> _sseClients = new();
    private readonly ConcurrentDictionary<int, Channel<string>> _matterEventClients = new();
    private readonly ControllerApiOptions _apiOptions = apiOptions.Value;
    private readonly CommissioningOptions _commissioningOptions = commissioningOptions.Value;
    private readonly DeviceCommandExecutor _commandExecutor = new(log, registry, apiOptions);
    private int _nextClientId;
    private int _nextMatterEventClientId;

    internal sealed record DeviceCommissioningStateQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceCommissioningStateResponse? Response,
        string? Error = null);

    internal sealed record DeviceOperationalCredentialsQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceOperationalCredentialsResponse? Response,
        string? Error = null);

    internal sealed record DeviceNetworkCommissioningQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceNetworkCommissioningStateResponse? Response,
        string? Error = null);

    internal sealed record DeviceNetworkCommissioningScanResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceNetworkCommissioningScanResponse? Response,
        string? Error = null);

    internal sealed record DeviceNetworkCommissioningCommandResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceNetworkCommissioningCommandResponse? Response,
        string? Error = null);

    internal sealed record DeviceNetworkCommissioningConnectResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceNetworkCommissioningConnectResponse? Response,
        string? Error = null);

    internal sealed record DeviceNetworkInterfaceWriteResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceNetworkInterfaceWriteResponse? Response,
        string? Error = null);

    internal sealed record DeviceGroupsQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceGroupsStateResponse? Response,
        string? Error = null);

    internal sealed record DeviceGroupKeyManagementQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceGroupKeyManagementStateResponse? Response,
        string? Error = null);

    internal sealed record DeviceScenesQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceScenesStateResponse? Response,
        string? Error = null);

    internal sealed record DeviceGroupCommandResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceGroupCommandResponse? Response,
        string? Error = null);

    internal sealed record DeviceGroupMembershipResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceGroupMembershipResponse? Response,
        string? Error = null);

    internal sealed record DeviceInvokeOnlyCommandResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceInvokeOnlyCommandResponse? Response,
        string? Error = null);

    internal sealed record DeviceGroupKeySetReadResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceGroupKeySetReadResponse? Response,
        string? Error = null);

    internal sealed record DeviceGroupKeySetIndicesResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceGroupKeySetIndicesResponse? Response,
        string? Error = null);

    internal sealed record DeviceSceneCommandResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceSceneCommandResponse? Response,
        string? Error = null);

    internal sealed record DeviceSceneViewResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceSceneViewResponse? Response,
        string? Error = null);

    internal sealed record DeviceSceneMembershipResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceSceneMembershipResponse? Response,
        string? Error = null);

    internal sealed record DeviceSceneCopyResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceSceneCopyResponse? Response,
        string? Error = null);

    /// <summary>Returns whether the device is known to the controller.</summary>
    public bool HasDevice(string id) => Sessions.ContainsKey(id) || Registry.Get(id) != null;

    /// <summary>Connect to a newly commissioned device using an owned background operation.</summary>
    public bool TryScheduleConnectNewDevice(string deviceId)
        => ScheduleOwnedOperation($"connect-device:{deviceId}", async ct =>
        {
            try
            {
                Registry.LoadFromDisk();
                if (Log.IsEnabled(LogLevel.Information))
                {
                    Log.LogInformation("Connecting to newly commissioned device {Id}...", deviceId);
                }

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(_commissioningOptions.FollowUpConnectTimeout);

                var rs = await CreateResilientSessionAsync(deviceId);
                if (rs != null)
                {
                    Sessions[deviceId] = rs;
                    if (await rs.ConnectAsync(connectCts.Token))
                    {
                        EnsureListenerRunning(deviceId, rs, ct);
                        PublishEvent(deviceId, "commissioned", "online");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Failed to connect newly commissioned device {Id}.", deviceId);
            }
        });

    /// <inheritdoc />
    protected override bool TryScheduleManagedReconnect(string id, ResilientSession rs)
    {
        DotMatterProductDiagnostics.RecordManagedReconnectRequest();
        return base.TryScheduleManagedReconnect(id, rs);
    }

    private bool TryGetConnectedSession(string id, out ResilientSession session)
    {
        if (Sessions.TryGetValue(id, out var currentSession) && currentSession.Session is not null)
        {
            session = currentSession;
            return true;
        }

        session = default!;
        return false;
    }

    private bool TryGetSessionOwner(string id, out ResilientSession session)
        => Sessions.TryGetValue(id, out session!);

    private ResilientSession GetRequiredSessionOwner(string id)
        => Sessions.TryGetValue(id, out var session)
            ? session
            : throw new InvalidOperationException($"Device '{id}' is not connected.");

    private async Task<DeviceOperationResult> ExecuteAdministrativeCommandAsync(
        string id,
        string operationName,
        Func<CancellationToken, Task<InvokeResponse>> executeAsync,
        string successMessage)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        return await _commandExecutor.ExecuteAsync(
            id,
            operationName,
            executeAsync,
            device => device.LastSeen = DateTime.UtcNow,
            $"{operationName}:{successMessage}",
            value => PublishEvent(id, "admin", value),
            $"Device {id}: {successMessage}",
            () =>
            {
                TryScheduleManagedReconnect(id, session);
                return Task.CompletedTask;
            });
    }
}
