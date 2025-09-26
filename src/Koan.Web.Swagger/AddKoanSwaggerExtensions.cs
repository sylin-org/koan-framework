using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Modules;
using Koan.Web.Swagger.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace Koan.Web.Swagger;

public static class AddKoanSwaggerExtensions
{
    public static IServiceCollection AddKoanSwagger(this IServiceCollection services, IConfiguration? config = null)
    {
        // Idempotency: if Swagger services are already registered, skip to avoid duplicate docs/config actions
        if (services.Any(d => d.ServiceType == typeof(ISwaggerProvider)))
        {
            return services;
        }
        if (config is null)
        {
            // Delay resolve until app builds; rely on DI at UseKoanSwagger time if needed.
            // For Add phase, prefer having config passed in; otherwise, we skip config-dependent parts.
        }
        // Bind typed options (lazy binding if config is not provided now)
        try { services.AddKoanOptions<KoanWebSwaggerOptions>(Infrastructure.Constants.Configuration.Section); } catch { }
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Koan API", Version = "v1" });
            var opts = config is not null ? GetOptions(config) : new KoanWebSwaggerOptions();
            if (opts.IncludeXmlComments)
            {
                foreach (var xml in GetXmlDocFiles())
                {
                    try { c.IncludeXmlComments(xml, includeControllerXmlComments: true); } catch { }
                }
            }
            // Document pagination semantics for EntityController endpoints
            c.OperationFilter<PaginationOperationFilter>();
            // Document common headers
            c.OperationFilter<KoanHeadersOperationFilter>();
            // If transformers assembly is present, include an operation filter to advertise alternate media types
            if (Type.GetType("Koan.Web.Transformers.EnableEntityTransformersAttribute, Koan.Web.Transformers") is not null)
            {
                c.OperationFilter<TransformerMediaTypesOperationFilter>();
            }
        });
        return services;
    }

    public static WebApplication UseKoanSwagger(this WebApplication app)
    {
        var env = app.Environment;
        var cfg = app.Configuration;
        var optsMon = app.Services.GetService<IOptions<KoanWebSwaggerOptions>>();
        var opts = optsMon?.Value ?? GetOptions(cfg);

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
            // Adding the module indicates intent to use it; enable by default outside Production
            enabled = true;
        }

        if (!enabled) return app; // off in non-dev unless explicitly enabled via env or magic flag

        // Ensure services were registered; if not, skip to avoid runtime 500s
        var provider = app.Services.GetService<ISwaggerProvider>();
        if (provider is null)
        {
            app.Logger.LogWarning("Koan.Web.Swagger: Swagger services not found. Did you call services.AddKoanSwagger()? Skipping UI middleware.");
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(ui =>
        {
            ui.RoutePrefix = opts.RoutePrefix;
            ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Koan API v1");
        });

        // Optionally protect UI outside Development
        if (!KoanEnv.IsDevelopment && opts.RequireAuthOutsideDevelopment)
        {
            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments($"/{opts.RoutePrefix}"), b =>
            {
                b.UseAuthentication();
                b.UseAuthorization();
            });
        }

        return app;
    }

    private static KoanWebSwaggerOptions GetOptions(IConfiguration cfg)
    {
        var o = new KoanWebSwaggerOptions();
        // ADR-0040: read explicit keys instead of binding
        o.Enabled = cfg.Read<bool?>(Constants.Configuration.Enabled);
        o.RoutePrefix = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RoutePrefix}", o.RoutePrefix)!;
        o.IncludeXmlComments = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.IncludeXmlComments}", o.IncludeXmlComments);
        o.RequireAuthOutsideDevelopment = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RequireAuthOutsideDevelopment}", o.RequireAuthOutsideDevelopment);
        // magic flag unified across Koan
        var magic = cfg.Read<bool?>(Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction);
        if (magic == true) o.Enabled = true;
        return o;
    }

    private static IEnumerable<string> GetXmlDocFiles()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var file in Directory.EnumerateFiles(baseDir, "*.xml", SearchOption.TopDirectoryOnly))
            yield return file;
    }
}

// Lives in Swagger assembly and uses reflection to talk to Transformers without a direct reference