using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;

namespace Koan.Web.Auth.Connector.Oidc.Initialization;

// CORE-0091: must Initialize after Koan.Web.Auth so the auth scheme
// registration this connector contributes is read against the
// AddAuthentication() builder Koan.Web.Auth has already created.
[After(typeof(Koan.Web.Auth.Initialization.AuthModule))]
public sealed class OidcAuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Intentionally empty: OIDC defaults are declared by provider contributors in consuming packages.
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddSetting(
            "ProviderContribution",
            "generic OIDC",
            source: BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" });
        module.AddSetting(
            "Defaults.Enabled",
            "false (requires explicit provider entry)",
            source: BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" });
    }
}


