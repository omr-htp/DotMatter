using DotMatter.Controller.Configuration;
using DotMatter.Core.InteractionModel;
using DotMatter.Hosting.Devices;
using Microsoft.Extensions.Options;

namespace DotMatter.Controller.Matter;

internal sealed class DeviceCommandExecutor(
    ILogger log,
    DeviceRegistry registry,
    IOptions<ControllerApiOptions> apiOptions)
{
    private readonly ControllerApiOptions _apiOptions = apiOptions.Value;

    public async Task<DeviceOperationResult> ExecuteAsync(
        string id,
        string operationName,
        Func<CancellationToken, Task<InvokeResponse>> sendAsync,
        Action<DeviceInfo> updateDevice,
        string eventValue,
        Action<string> publishEvent,
        string successLogMessage,
        Func<Task>? onTransportFailure = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var response = await sendAsync(cts.Token);
            if (!response.Success)
            {
                log.LogWarning("Device {Id}: {Operation} failed (status 0x{Status:X2})", id, operationName, response.StatusCode);
                return new DeviceOperationResult(false, DeviceOperationFailure.TransportError, $"Matter status 0x{response.StatusCode:X2}");
            }

            registry.Update(id, updateDevice);
            log.LogInformation("{Message}", successLogMessage);
            publishEvent(eventValue);
            return DeviceOperationResult.Succeeded;
        }
        catch (OperationCanceledException)
        {
            log.LogError("Device {Id}: {Operation} timed out", id, operationName);
            if (onTransportFailure != null)
            {
                await onTransportFailure();
            }

            return new DeviceOperationResult(false, DeviceOperationFailure.Timeout, $"{operationName} timed out");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Device {Id}: {Operation} failed", id, operationName);
            if (onTransportFailure != null)
            {
                await onTransportFailure();
            }

            return new DeviceOperationResult(false, DeviceOperationFailure.TransportError, ex.Message);
        }
    }
}
