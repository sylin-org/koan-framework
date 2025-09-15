using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Auth.Microsoft.Providers;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Microsoft.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Microsoft";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, MicrosoftProviderContributor>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("Provides", "microsoft (OIDC)");
    }
}
