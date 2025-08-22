using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Core.Extensions;
using Sora.Web.Swagger.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace Sora.Web.Swagger.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Swagger";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Guard to prevent duplicate Swagger registration if the app already called AddSoraSwagger()
        if (!services.Any(d => d.ServiceType == typeof(ISwaggerProvider)))
        {
            services.AddSoraSwagger();
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.SoraSwaggerStartupFilter>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        // ADR-0040: read settings via helper and constants
        var enabled = cfg.Read(Constants.Configuration.Enabled, SoraEnv.IsProduction ? false : true);
        var routePrefix = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RoutePrefix}", "swagger");
        var requireAuth = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RequireAuthOutsideDevelopment}", true);
        var xml = cfg.Read($"{Constants.Configuration.Section}:{Constants.Configuration.Keys.IncludeXmlComments}", true);
        // Magic flag can force-enable in production
        var magic = cfg.Read(Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        if (magic) enabled = true;
        report.AddSetting("Enabled", enabled.ToString());
        report.AddSetting("RoutePrefix", routePrefix);
        report.AddSetting("RequireAuthOutsideDevelopment", requireAuth.ToString());
        report.AddSetting("IncludeXmlComments", xml.ToString());
    }
}
