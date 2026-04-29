namespace DotMatter.Controller;

internal static class EventApiEndpoints
{
    internal static void MapEventApiEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/events", async (HttpContext context, MatterControllerService service, CancellationToken ct) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await using var subscription = service.SubscribeEvents(ct);
            await foreach (var evt in subscription.Reader.ReadAllAsync(ct))
            {
                await context.Response.WriteAsync($"data: {evt}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        })
            .WithTags("Devices")
            .WithSummary("Subscribe to events via SSE")
            .WithDescription("Server-Sent Events stream for real-time device state changes.")
            .ExcludeFromDescription();
    }
}
