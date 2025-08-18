using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Swashbuckle.AspNetCore.Swagger;

namespace Sora.Web.Swagger.Hosting;

// Auto-register Swagger when the assembly is referenced
// legacy initializer removed in favor of standardized auto-registrar

internal sealed class SoraSwaggerStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            const string appliedKey = "Sora.Web.Swagger.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;

                // Determine if Swagger should be enabled
                var env = app.ApplicationServices.GetService<IHostEnvironment>();
                var cfg = app.ApplicationServices.GetService<IConfiguration>();
                var opts = GetOptions(cfg);

                bool enabled;
                if (opts.Enabled.HasValue)
                {
                    enabled = opts.Enabled.Value;
                }
                else if (env?.IsProduction() == true)
                {
                    enabled = cfg?.GetValue<bool?>("Sora__Web__Swagger__Enabled") == true
                           || cfg?.GetValue<bool?>("Sora:AllowMagicInProduction") == true;
                }
                else
                {
                    // Referencing the module indicates intent to use it; enable by default outside Production
                    enabled = true;
                }

                if (enabled)
                {
                    // Ensure services were registered; if not, skip to avoid runtime 500s
                    var provider = app.ApplicationServices.GetService<ISwaggerProvider>();
                    if (provider is not null)
                    {
                        // Apply Swagger middleware
                        app.UseSwagger();
                        app.UseSwaggerUI(ui =>
                        {
                            ui.RoutePrefix = opts.RoutePrefix;
                            ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Sora API v1");
                        });

                        // Optionally require auth outside Development
                        if (env?.IsDevelopment() != true && opts.RequireAuthOutsideDevelopment)
                        {
                            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments($"/{opts.RoutePrefix}"), b =>
                            {
                                b.UseAuthentication();
                                b.UseAuthorization();
                            });
                        }
                    }
                }
            }

            next(app);
        };
    }

    private static SoraWebSwaggerOptions GetOptions(IConfiguration? cfg)
    {
        var o = new SoraWebSwaggerOptions();
        try { cfg?.GetSection("Sora:Web:Swagger").Bind(o); } catch { }
        var envEnabled = cfg?.GetValue<bool?>("Sora__Web__Swagger__Enabled");
        if (envEnabled.HasValue) o.Enabled = envEnabled;
        var magic = cfg?.GetValue<bool?>("Sora:AllowMagicInProduction");
        if (magic == true) o.Enabled = true;
        return o;
    }
}
