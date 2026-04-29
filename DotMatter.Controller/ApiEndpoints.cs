namespace DotMatter.Controller;

internal static class ApiEndpoints
{
    internal static void MapApiEndpoints(this WebApplication app, string rateLimitPolicy)
    {
        var api = app.MapGroup("/api").RequireRateLimiting(rateLimitPolicy);
        api.MapDeviceApiEndpoints();
        api.MapCommissioningApiEndpoints();
        api.MapEventApiEndpoints();
    }
}
