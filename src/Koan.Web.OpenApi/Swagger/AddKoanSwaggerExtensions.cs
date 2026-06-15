using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Modules;
using Koan.Web.Connector.Swagger.Infrastructure;
using Koan.Web.OpenApi.Options;
using Swashbuckle.AspNetCore.SwaggerUI;
using Microsoft.AspNetCore.OpenApi;

namespace Koan.Web.Connector.Swagger;

public static class AddKoanSwaggerExtensions
{
    public static IServiceCollection AddKoanSwagger(this IServiceCollection services, IConfiguration? config = null)
    {
        // Bind typed options (lazy binding if config is not provided now)
        try { services.AddKoanOptions<KoanWebSwaggerOptions>(Infrastructure.Constants.Configuration.Section); } catch { }

        services.AddEndpointsApiExplorer();
        services.AddOpenApi();

        return services;
    }

    public static WebApplication UseKoanSwagger(this WebApplication app)
    {
        var env = app.Environment;
        var cfg = app.Configuration;
        var optsMon = app.Services.GetService<IOptions<KoanWebSwaggerOptions>>();
        var opts = optsMon?.Value ?? GetOptions(cfg);

        bool enabled;
        var enableUi = cfg.Read<bool?>($"{KoanOpenApiOptions.ConfigurationSection}:EnableUi");
        if (enableUi.HasValue)
        {
            enabled = enableUi.Value;
        }
        else if (opts.Enabled.HasValue)
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

        app.MapOpenApi("openapi/{documentName}.json");

        app.UseSwaggerUI(ui =>
        {
            ui.RoutePrefix = opts.RoutePrefix;
            ui.SwaggerEndpoint($"/openapi/{KoanOpenApiOptions.DefaultDocumentName}.json", "Koan API v1");
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

}

