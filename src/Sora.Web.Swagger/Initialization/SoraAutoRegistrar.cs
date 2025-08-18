using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
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
        var o = new SoraWebSwaggerOptions();
        cfg.GetSection("Sora:Web:Swagger").Bind(o);
        var envEnabled = cfg.GetValue<bool?>("Sora__Web__Swagger__Enabled");
        if (envEnabled.HasValue) o.Enabled = envEnabled;
        var magic = cfg.GetValue<bool?>("Sora:AllowMagicInProduction");
        if (magic == true) o.Enabled = true;
        report.AddSetting("Enabled", (o.Enabled ?? !env.IsProduction()).ToString());
        report.AddSetting("RoutePrefix", o.RoutePrefix);
        report.AddSetting("RequireAuthOutsideDevelopment", o.RequireAuthOutsideDevelopment.ToString());
        report.AddSetting("IncludeXmlComments", o.IncludeXmlComments.ToString());
    }
}
