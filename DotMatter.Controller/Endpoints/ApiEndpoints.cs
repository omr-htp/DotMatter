namespace DotMatter.Controller.Endpoints;

internal static class ApiEndpoints
{
    internal static void MapApiEndpoints(this WebApplication app, string rateLimitPolicy)
    {
        var api = app.MapGroup("/api").RequireRateLimiting(rateLimitPolicy);
        api.MapSystemApiEndpoints();
        api.MapDiscoveryApiEndpoints();
        api.MapDeviceApiEndpoints();
        api.MapCommissioningApiEndpoints();
        api.MapEventApiEndpoints();
    }
}
