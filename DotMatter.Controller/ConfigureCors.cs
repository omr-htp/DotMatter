using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace DotMatter.Controller;

internal sealed class ConfigureCors(IOptions<ControllerSecurityOptions> security)
    : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions opts)
    {
        var origins = security.Value.AllowedCorsOrigins;
        if (origins.Length > 0)
        {
            opts.AddPolicy("lan", p =>
                p.WithOrigins(origins)
                 .AllowAnyMethod()
                 .AllowAnyHeader());
        }
    }
}
