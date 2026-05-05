using DotMatter.Ui.Components;
using DotMatter.Ui.Services;
using Radzen;

namespace DotMatter.Ui;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddRadzenComponents();
        builder.Services.AddScoped<DialogService>();
        builder.Services.AddScoped<NotificationService>();
        builder.Services.AddScoped<TooltipService>();
        builder.Services.AddScoped<ContextMenuService>();
        builder.Services.AddOptions<ControllerApiOptions>()
            .Bind(builder.Configuration.GetSection("ControllerApi"));
        builder.Services.AddHttpClient<ControllerApiClient>((services, client) =>
        {
            var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ControllerApiOptions>>().Value;
            client.BaseAddress = new Uri(NormalizeBaseUrl(options.BaseUrl));
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();

        app.MapGet("/ui/live/controller", async (HttpContext context, ControllerApiClient api) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Append("X-Accel-Buffering", "no");
            context.Response.ContentType = "text/event-stream";
            await context.Response.Body.FlushAsync(context.RequestAborted);

            await api.StreamAsync("/api/events", async (message, ct) =>
            {
                await context.Response.WriteAsync($"data: {message}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }, context.RequestAborted);
        });

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://localhost:5000"
            : baseUrl.Trim();

        return trimmed.EndsWith('/')
            ? trimmed
            : $"{trimmed}/";
    }
}
