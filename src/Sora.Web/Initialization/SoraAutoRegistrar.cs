using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Core.Extensions;
using Sora.Web.Extensions;

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
        var secure = cfg.Read($"{Sora.Web.Infrastructure.ConfigurationConstants.Web.Section}:{Sora.Web.Infrastructure.ConfigurationConstants.Web.Keys.EnableSecureHeaders}", true);
        var proxied = cfg.Read($"{Sora.Web.Infrastructure.ConfigurationConstants.Web.Section}:{Sora.Web.Infrastructure.ConfigurationConstants.Web.Keys.IsProxiedApi}", false);
        var csp = cfg.Read<string?>($"{Sora.Web.Infrastructure.ConfigurationConstants.Web.Section}:{Sora.Web.Infrastructure.ConfigurationConstants.Web.Keys.ContentSecurityPolicy}");
        var autoMap = cfg.Read($"{Sora.Web.Infrastructure.ConfigurationConstants.Web.Section}:{Sora.Web.Infrastructure.ConfigurationConstants.Web.Keys.AutoMapControllers}", true);
        report.AddSetting("EnableSecureHeaders", secure.ToString());
        report.AddSetting("IsProxiedApi", proxied.ToString());
        report.AddSetting("AutoMapControllers", autoMap.ToString());
        if (!string.IsNullOrWhiteSpace(csp)) report.AddSetting("ContentSecurityPolicy", $"len={csp.Length}");
    }
}
