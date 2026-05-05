using DotMatter.Controller;
using DotMatter.Controller.Configuration;
using DotMatter.Controller.Diagnostics;
using DotMatter.Controller.Endpoints;
using DotMatter.Controller.Matter;
using DotMatter.Core;
using DotMatter.Hosting.Devices;
using DotMatter.Hosting.Runtime;
using DotMatter.Hosting.Thread;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

const string CorsPolicyName = "lan";
const string RateLimitPolicyName = "api";
const string ApiKeySecuritySchemeName = "ApiKey";

var builder = WebApplication.CreateBuilder(args);
var openApiSecurity = ServiceCollectionExtensions.NormalizeSecurityOptions(
    builder.Configuration.GetSection("Controller:Security").Get<ControllerSecurityOptions>() ?? new());

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
builder.Services.AddSingleton<ICommissionableDeviceDiscoveryService, CommissionableDeviceDiscoveryService>();
builder.Services.AddSingleton<CommissioningService>();
builder.Services.AddSingleton<MatterControllerService>();
builder.Services.AddSingleton<RuntimeDiagnosticsService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatterControllerService>());
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    if (!openApiSecurity.RequireApiKey)
    {
        return;
    }

    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[ApiKeySecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = openApiSecurity.HeaderName,
            Description = $"Provide the controller API key using the {openApiSecurity.HeaderName} header."
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        var relativePath = context.Description.RelativePath ?? string.Empty;
        if (relativePath.StartsWith("health", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiKeySecuritySchemeName, context.Document, null)] = []
        });

        return Task.CompletedTask;
    });
});
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

if (security.AllowedCorsOrigins.Length > 0)
{
    app.UseCors(CorsPolicyName);
}

app.Use(async (ctx, next) =>
{
    // Health and API reference endpoints stay unauthenticated so probes and human operators can load them.
    if (!security.RequireApiKey
        || ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || ctx.Request.Path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase)
        || ctx.Request.Path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase))
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

app.UseRateLimiter();

if (api.EnableOpenApi)
{
    app.MapOpenApi();
    app.MapScalarApiReference(opts =>
    {
        opts.WithTitle("Matter Controller API");
        opts.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        if (security.RequireApiKey)
        {
            opts.AddApiKeyAuthentication(ApiKeySecuritySchemeName, apiKey => apiKey.WithName(security.HeaderName));
            opts.AddPreferredSecuritySchemes([ApiKeySecuritySchemeName]);
        }
    });
}

app.MapApiEndpoints(RateLimitPolicyName);
app.MapHealthEndpoints();

app.Run();
