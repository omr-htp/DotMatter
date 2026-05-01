namespace DotMatter.Controller;

internal static class SystemApiEndpoints
{
    internal static void MapSystemApiEndpoints(this RouteGroupBuilder api)
    {
        var system = api.MapGroup("/system").WithTags("System");

        system.MapGet("/runtime", (RuntimeDiagnosticsService diagnostics) =>
            Results.Json(
                diagnostics.GetRuntimeSnapshot(),
                ControllerJsonContext.Default.RuntimeSnapshotResponse))
            .WithSummary("Get runtime status")
            .WithDescription("Returns a safe authenticated runtime snapshot for the controller service, including readiness state, uptime, device counts, and in-process diagnostic counters.");

        system.MapGet("/diagnostics", (RuntimeDiagnosticsService diagnostics) =>
        {
            if (!diagnostics.IsDetailedEndpointEnabled())
            {
                return Results.Json(
                    new ErrorResponse("Detailed runtime diagnostics are disabled"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(
                diagnostics.GetDetailedDiagnostics(),
                ControllerJsonContext.Default.RuntimeDetailedResponse);
        })
            .WithSummary("Get detailed diagnostics")
            .WithDescription("Returns a more detailed runtime diagnostics payload when explicitly enabled by configuration. Disabled by default.");
    }
}
