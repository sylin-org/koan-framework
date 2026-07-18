using Koan.Core;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Auth.Connector.Google.Initialization;

public sealed class GoogleAuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddSingleton(AuthProviderDefinition.Oidc(
            "google",
            "Google",
            "/icons/google.svg",
            "https://accounts.google.com",
            ["openid", "email", "profile"],
            priority: 200));

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);
}
