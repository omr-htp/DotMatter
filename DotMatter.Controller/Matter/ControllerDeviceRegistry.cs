using DotMatter.Controller.Diagnostics;
using DotMatter.Hosting.Devices;
using Microsoft.Extensions.Options;

namespace DotMatter.Controller.Matter;

/// <summary>
/// Controller-specific registry wrapper that emits product diagnostics for persistence failures.
/// </summary>
internal sealed class ControllerDeviceRegistry(
    ILogger<DeviceRegistry> logger,
    IOptions<RegistryOptions> options) : DeviceRegistry(logger, options)
{
    protected override void OnPersistenceFailure(string id, string operation, Exception exception)
    {
        DotMatterProductDiagnostics.RecordRegistryPersistenceFailure();
    }
}
