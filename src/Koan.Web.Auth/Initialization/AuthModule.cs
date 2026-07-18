using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;
using Koan.Web.Auth.Composition;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Hosting;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Pillars;
using Koan.Web.Auth.Providers;
using Koan.Web.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Initialization;

[After(typeof(Koan.Web.Initialization.WebModule))]
public sealed class AuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        SecurityPillarManifest.EnsureRegistered();
        services.AddKoanWebAuth();
        services.AddHttpContextAccessor();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Web.Hosting.IKoanWebPipelineContributor, DevIdentityContributor>());
        services.AddKoanControllersFrom<Controllers.DiscoveryController>();
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var plan = services.GetRequiredService<AuthProviderPlan>();
        AuthSchemeSeeder.Seed(services);

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Web.Auth");
        logger?.LogInformation(
            "Koan Web Auth provider plan compiled: available={Available} eligible={Eligible} default={Default} reason={Reason}",
            plan.Providers.Count,
            plan.Providers.Count(static provider => provider.Eligible),
            plan.Default?.Id ?? "none",
            plan.Default?.Reason ?? "no-eligible-provider");

        return Task.CompletedTask;
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddTool(
            "Auth Provider Discovery",
            AuthConstants.Routes.Discovery,
            "Lists eligible authentication providers",
            capability: "auth.discovery");
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        => AuthCompositionFacts.Project(composition, services, Id);
}
