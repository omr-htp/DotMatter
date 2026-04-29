using DotMatter.Hosting;

namespace DotMatter.Controller;

internal static class HealthEndpoints
{
    internal static void MapHealthEndpoints(this WebApplication app)
    {
        var runtimeStatus = app.Services.GetRequiredService<MatterRuntimeStatus>();

        app.MapGet("/health/live", () =>
            Results.Ok(new LivenessResponse("alive", DateTime.UtcNow)))
            .WithTags("System")
            .WithSummary("Liveness check");

        app.MapGet("/health/ready", () =>
        {
            var ready = runtimeStatus.IsReady;
            var payload = new ReadinessResponse(
                ready ? "ready" : "not-ready",
                ready,
                runtimeStatus.LastStartupError,
                DateTime.UtcNow);

            return ready
                ? Results.Ok(payload)
                : Results.Json(payload, ControllerJsonContext.Default.ReadinessResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .WithTags("System")
            .WithSummary("Readiness check");

        app.MapGet("/health", (DeviceRegistry reg) =>
        {
            var devices = reg.GetAll().ToList();
            var online = devices.Count(d => d.IsOnline);
            var uptime = DateTime.UtcNow - runtimeStatus.StartedAtUtc;
            var status = runtimeStatus.IsReady ? "healthy" : "degraded";

            var payload = new HealthResponse(
                status,
                uptime.ToString(@"d\.hh\:mm\:ss"),
                runtimeStatus.IsReady,
                new DeviceCounts(devices.Count, online),
                DateTime.UtcNow,
                runtimeStatus.LastStartupError);

            return runtimeStatus.IsReady
                ? Results.Ok(payload)
                : Results.Json(payload, ControllerJsonContext.Default.HealthResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .WithTags("System")
            .WithSummary("Health check")
            .WithDescription("Returns service health with readiness and device connectivity summary.");
    }
}
