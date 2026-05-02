using DotMatter.Core;
using DotMatter.Hosting;
using Microsoft.Extensions.Options;

namespace DotMatter.Controller;

internal sealed class RuntimeDiagnosticsService(
    DeviceRegistry registry,
    MatterRuntimeStatus runtimeStatus,
    IHostEnvironment hostEnvironment,
    IOptions<ControllerSecurityOptions> security,
    IOptions<ControllerApiOptions> api,
    IOptions<CommissioningOptions> commissioning,
    IOptions<ControllerDiagnosticsOptions> diagnostics,
    IOptions<MatterLogSettings> matterLog)
{
    private readonly ControllerSecurityOptions _security = security.Value;
    private readonly ControllerApiOptions _api = api.Value;
    private readonly CommissioningOptions _commissioning = commissioning.Value;
    private readonly ControllerDiagnosticsOptions _diagnostics = diagnostics.Value;
    private readonly MatterLogSettings _matterLog = matterLog.Value;

    internal RuntimeSnapshotResponse GetRuntimeSnapshot()
    {
        var devices = registry.GetAll().ToList();
        return new RuntimeSnapshotResponse(
            GetStatus(runtimeStatus),
            hostEnvironment.EnvironmentName,
            runtimeStatus.StartupCompleted,
            runtimeStatus.IsReady,
            runtimeStatus.IsStopping,
            (DateTime.UtcNow - runtimeStatus.StartedAtUtc).ToString(@"d\.hh\:mm\:ss"),
            runtimeStatus.StartedAtUtc,
            new DeviceCounts(devices.Count, devices.Count(static device => device.IsOnline)),
            new RuntimeDiagnosticsCounters(
                DotMatterProductDiagnostics.CommissioningAttemptCount,
                DotMatterProductDiagnostics.CommissioningRejectionCount,
                DotMatterProductDiagnostics.ApiAuthenticationFailureCount,
                DotMatterProductDiagnostics.RateLimitRejectionCount,
                DotMatterProductDiagnostics.ManagedReconnectRequestCount,
                DotMatterProductDiagnostics.SubscriptionRestartCount,
                DotMatterProductDiagnostics.RegistryPersistenceFailureCount),
            DateTime.UtcNow,
            runtimeStatus.LastStartupError);
    }

    internal bool IsDetailedEndpointEnabled()
        => _diagnostics.EnableDetailedRuntimeEndpoint;

    internal RuntimeDetailedResponse GetDetailedDiagnostics()
        => new(
            GetRuntimeSnapshot(),
            new RuntimeApiDiagnostics(
                _security.RequireApiKey,
                _security.HeaderName,
                _security.AllowedCorsOrigins.Length,
                _api.RateLimitPermitLimit,
                _api.RateLimitWindow.ToString(),
                _api.RateLimitQueueLimit,
                _api.SseClientBufferCapacity,
                _api.CommandTimeout.ToString(),
                _api.EnableOpenApi),
            new RuntimeDetailedDiagnostics(
                _diagnostics.EnableDetailedRuntimeEndpoint,
                _matterLog.EnableSensitiveDiagnostics,
                _matterLog.MaxRenderedBytes,
                _commissioning.SharedFabricName,
                _commissioning.DefaultFabricNamePrefix,
                _commissioning.FollowUpConnectTimeout.ToString(),
                _commissioning.RegulatoryLocation.ToString(),
                _commissioning.RegulatoryCountryCode,
                _commissioning.AttestationPolicy.ToString()));

    private static string GetStatus(MatterRuntimeStatus runtimeStatus)
    {
        if (runtimeStatus.IsStopping)
        {
            return "stopping";
        }

        if (runtimeStatus.IsReady)
        {
            return "ready";
        }

        return runtimeStatus.StartupCompleted ? "degraded" : "starting";
    }
}
