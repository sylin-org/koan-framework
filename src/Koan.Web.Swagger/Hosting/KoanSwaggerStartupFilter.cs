using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Web.Swagger.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace Koan.Web.Swagger.Hosting;

// Auto-register Swagger when the assembly is referenced
// Startup filter remains; auto-registration is provided via Initialization/KoanAutoRegistrar.

internal sealed class KoanSwaggerStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            const string appliedKey = "Koan.Web.Swagger.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;

                // Determine if Swagger should be enabled
                var env = app.ApplicationServices.GetService<IHostEnvironment>();
                var cfg = app.ApplicationServices.GetService<IConfiguration>();
                var logger = app.ApplicationServices.GetService<ILogger<KoanSwaggerStartupFilter>>();
                var opts = GetOptions(cfg);

                bool enabled;
                if (opts.Enabled.HasValue)
                {
                    enabled = opts.Enabled.Value;
                }
                else if (KoanEnv.IsProduction)
                {
                    enabled = cfg.Read(Constants.Configuration.Enabled, false)
                  || cfg.Read(Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction, false);
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
                            ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Koan API v1");
                        });

                        logger?.LogInformation("Koan.Web.Swagger enabled at '/{RoutePrefix}' (env={Env})", opts.RoutePrefix, env?.EnvironmentName);

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
                    else
                    {
                        logger?.LogWarning("Koan.Web.Swagger requested but services were not registered. Call services.AddKoanSwagger() earlier or keep the auto-registrar.");
                    }
                }
                else
                {
                    logger?.LogInformation("Koan.Web.Swagger disabled (env={Env}). Set 'Koan:Web:Swagger:Enabled=true' or 'Koan:AllowMagicInProduction=true' to enable.", env?.EnvironmentName);
                }
            }

            next(app);
        };
    }

    private static KoanWebSwaggerOptions GetOptions(IConfiguration? cfg)
    {
        var o = new KoanWebSwaggerOptions();
        if (cfg is not null)
        {
            // ADR-0040: read individual keys
            o.Enabled = cfg.Read<bool?>(Constants.Configuration.Enabled);
            o.RoutePrefix = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RoutePrefix}", o.RoutePrefix)!;
            o.IncludeXmlComments = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.IncludeXmlComments}", o.IncludeXmlComments);
            o.RequireAuthOutsideDevelopment = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RequireAuthOutsideDevelopment}", o.RequireAuthOutsideDevelopment);
        }
        var magic = cfg.Read<bool?>(Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction);
        if (magic == true) o.Enabled = true;
        return o;
    }
}
