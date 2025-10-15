using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Web.Auth.Connector.Google.Providers;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Connector.Google.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Connector.Google";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, GoogleProviderContributor>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddSetting(
            "Provider",
            "google (OIDC)",
            source: BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" });
        module.AddSetting(
            "Defaults.Enabled",
            "true",
            source: BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" });
    }
}


