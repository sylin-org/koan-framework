using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;
using Koan.Web.Auth.Connector.Microsoft.Providers;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Connector.Microsoft.Initialization;

// CORE-0091: must Initialize after Koan.Web.Auth so the auth scheme
// registration this connector contributes is read against the
// AddAuthentication() builder Koan.Web.Auth has already created.
[After(typeof(Koan.Web.Auth.Initialization.AuthModule))]
public sealed class MicrosoftAuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, MicrosoftProviderContributor>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddSetting(
            "Provider",
            "microsoft (OIDC)",
            source: BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" });
        module.AddSetting(
            "Defaults.Enabled",
            "true",
            source: BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" });
    }
}


