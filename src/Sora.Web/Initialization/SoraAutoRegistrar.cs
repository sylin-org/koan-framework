using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Web.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSoraWeb();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.SoraWebStartupFilter>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var section = cfg.GetSection("Sora:Web");
        var secure = section.GetValue<bool?>("EnableSecureHeaders") ?? true;
        var proxied = section.GetValue<bool?>("IsProxiedApi") ?? false;
        var csp = section.GetValue<string?>("ContentSecurityPolicy");
        var autoMap = section.GetValue<bool?>("AutoMapControllers") ?? true;
        report.AddSetting("EnableSecureHeaders", secure.ToString());
        report.AddSetting("IsProxiedApi", proxied.ToString());
        report.AddSetting("AutoMapControllers", autoMap.ToString());
        if (!string.IsNullOrWhiteSpace(csp)) report.AddSetting("ContentSecurityPolicy", $"len={csp.Length}");
    }
}
