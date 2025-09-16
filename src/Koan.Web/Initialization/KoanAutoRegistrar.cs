using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Web.Extensions;

namespace Koan.Web.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanWeb();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.KoanWebStartupFilter>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var secure = cfg.Read($"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.EnableSecureHeaders}", true);
        var proxied = cfg.Read($"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.IsProxiedApi}", false);
        var csp = cfg.Read<string?>($"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.ContentSecurityPolicy}");
        var autoMap = cfg.Read($"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.AutoMapControllers}", true);
        report.AddSetting("EnableSecureHeaders", secure.ToString());
        report.AddSetting("IsProxiedApi", proxied.ToString());
        report.AddSetting("AutoMapControllers", autoMap.ToString());
        if (!string.IsNullOrWhiteSpace(csp)) report.AddSetting("ContentSecurityPolicy", $"len={csp.Length}");
    }
}
