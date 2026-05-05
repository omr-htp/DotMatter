using System.Threading.Channels;

namespace DotMatter.Controller.Endpoints;

internal static class EventApiEndpoints
{
    internal static void MapEventApiEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/events", async (HttpContext context, MatterControllerService service, CancellationToken ct) =>
        {
            await using var subscription = service.SubscribeEvents(ct);
            ConfigureSseResponse(context);
            await WriteSseStreamAsync(context, subscription.Reader, ct);
        })
            .WithTags("Devices")
            .WithSummary("Subscribe to events via SSE")
            .WithDescription("Server-Sent Events stream for real-time device state changes.")
            .ExcludeFromDescription();

        api.MapGet("/matter/events", async (HttpContext context, MatterControllerService service, CancellationToken ct) =>
        {
            await using var subscription = service.SubscribeMatterEvents(ct);
            ConfigureSseResponse(context);
            await WriteSseStreamAsync(context, subscription.Reader, ct);
        })
            .WithTags("Devices")
            .WithSummary("Subscribe to live Matter events")
            .WithDescription("Server-Sent Events stream for raw Matter event envelopes observed from subscribed devices. This testing surface is separate from the controller's internal state-change stream.");
    }

    private static void ConfigureSseResponse(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
    }

    private static async Task WriteSseStreamAsync(HttpContext context, ChannelReader<string> reader, CancellationToken ct)
    {
        await context.Response.StartAsync(ct);
        await context.Response.WriteAsync(": connected\n\n", ct);
        await context.Response.Body.FlushAsync(ct);

        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                await context.Response.WriteAsync($"data: {evt}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (IOException) when (ct.IsCancellationRequested || context.RequestAborted.IsCancellationRequested)
        {
        }
    }
}
