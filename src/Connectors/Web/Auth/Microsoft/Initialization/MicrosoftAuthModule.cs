using Koan.Core;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Auth.Connector.Microsoft.Initialization;

public sealed class MicrosoftAuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddSingleton(AuthProviderDefinition.Oidc(
            "microsoft",
            "Microsoft",
            "/icons/microsoft.svg",
            "https://login.microsoftonline.com/common/v2.0",
            ["openid", "email", "profile"],
            priority: 200));

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);
}
