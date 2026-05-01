using DotMatter.Controller;
using DotMatter.Core;
using DotMatter.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

const string CorsPolicyName = "lan";
const string RateLimitPolicyName = "api";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSystemd();
builder.Logging.AddSimpleConsole(opts =>
{
    opts.SingleLine = true;
    opts.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddControllerOptions(builder.Configuration);

builder.Services.AddSingleton<MatterRuntimeStatus>();
builder.Services.AddSingleton<IOtbrService, OtbrService>();
builder.Services.AddSingleton<DeviceRegistry, ControllerDeviceRegistry>();
builder.Services.AddSingleton<CommissioningService>();
builder.Services.AddSingleton<MatterControllerService>();
builder.Services.AddSingleton<RuntimeDiagnosticsService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatterControllerService>());
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCors();
builder.Services.ConfigureOptions<ConfigureCors>();

builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.TypeInfoResolverChain.Insert(0, ControllerJsonContext.Default);
});

builder.Services.AddRateLimiter(_ => { });
builder.Services.ConfigureOptions<ConfigureRateLimiter>();

var app = builder.Build();

MatterLog.Init(app.Services.GetRequiredService<ILoggerFactory>());
MatterLog.Settings = app.Services.GetRequiredService<IOptions<MatterLogSettings>>().Value;

var security = app.Services.GetRequiredService<IOptions<ControllerSecurityOptions>>().Value;
var api = app.Services.GetRequiredService<IOptions<ControllerApiOptions>>().Value;

app.UseExceptionHandler(error => error.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ExceptionHandler");
    log.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

    var statusCode = ex is BadHttpRequestException badReq ? badReq.StatusCode : StatusCodes.Status500InternalServerError;
    ctx.Response.StatusCode = statusCode;
    await ctx.Response.WriteAsJsonAsync(
        new ErrorResponse(statusCode < 500 ? (ex?.Message ?? "Bad request") : "An internal error occurred"),
        ControllerJsonContext.Default.ErrorResponse);
}));
app.UseStatusCodePages();

app.Use(async (ctx, next) =>
{
    // Health endpoints stay unauthenticated so systemd, probes, and monitors can check readiness.
    if (!security.RequireApiKey || ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var provided = ctx.Request.Headers[security.HeaderName].FirstOrDefault();
    if (!string.Equals(provided, security.ApiKey, StringComparison.Ordinal))
    {
        DotMatterProductDiagnostics.RecordApiAuthenticationFailure();
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(
            new ErrorResponse("Invalid or missing API key"),
            ControllerJsonContext.Default.ErrorResponse);
        return;
    }

    await next();
});

if (security.AllowedCorsOrigins.Length > 0)
{
    app.UseCors(CorsPolicyName);
}

app.UseRateLimiter();

if (api.EnableOpenApi)
{
    app.MapOpenApi();
    app.MapScalarApiReference(opts =>
    {
        opts.WithTitle("Matter Controller API");
        opts.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapApiEndpoints(RateLimitPolicyName);
app.MapHealthEndpoints();

app.Run();
