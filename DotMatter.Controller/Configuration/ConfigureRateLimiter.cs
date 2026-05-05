using System.Threading.RateLimiting;
using DotMatter.Controller.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DotMatter.Controller.Configuration;

internal sealed class ConfigureRateLimiter(IOptions<ControllerApiOptions> api)
    : IConfigureOptions<RateLimiterOptions>
{
    public void Configure(RateLimiterOptions opts)
    {
        opts.RejectionStatusCode = 429;
        opts.OnRejected = (context, _) =>
        {
            DotMatterProductDiagnostics.RecordRateLimitRejection();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<ConfigureRateLimiter>>();
            logger.LogWarning(
                "Rate limit exceeded for {Method} {Path} from {RemoteIp}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress);
            return ValueTask.CompletedTask;
        };

        opts.AddFixedWindowLimiter("api", o =>
        {
            o.PermitLimit = api.Value.RateLimitPermitLimit;
            o.Window = api.Value.RateLimitWindow;
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            o.QueueLimit = api.Value.RateLimitQueueLimit;
        });
    }
}
