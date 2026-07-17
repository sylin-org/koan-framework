using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Identity.Access;
using Koan.Identity.Tenancy.Access;
using Koan.Identity.Tenancy.Deprovisioning;
using Koan.Identity.Tenancy.Invitations;
using Koan.Identity.Tenancy.Resolvers;
using Koan.Tenancy;
using Koan.Web.Hosting;

namespace Koan.Identity.Tenancy.Initialization;

/// <summary>
/// SEC-0007 Layer 4 — Reference = Intent: referencing <c>Koan.Identity.Tenancy</c> composes the durable person with
/// the tenancy control plane. It lights up membership-scoped effective access (the Membership contributor over the
/// SAME Layer-2 resolver), the four tenant-resolution carriers + the <c>AfterAuthentication</c> middleware that
/// scopes the request to a membership-authorized tenant, invite-binds-to-identity, and atomic verifiable
/// deprovisioning. Ordered <c>[After]</c> both pillars so their registrations exist first.
/// </summary>
[After(typeof(Koan.Identity.Initialization.SecIdentityModule))]
[After(typeof(Koan.Tenancy.Initialization.TenancyModule))]
public sealed class IdentityTenancyModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TenancyResolutionOptions>(TenancyResolutionOptions.SectionPath);

        // P2/P4 — the Membership effective-access contributor lights up over the SAME Layer-2 resolver (the
        // contributor-pipeline canon: no bespoke axis logic, no code-path fork). TryAddEnumerable dedupes with the
        // SecIdentityModule [KoanDiscoverable] scan if it also discovers this type.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEffectiveAccessContributor, MembershipAccessContributor>());

        // P4 — the four tenant-resolution carriers, registered in resolution order (claim → header → subdomain →
        // path). The middleware tries them in registration order and the first non-null candidate wins. Their mere
        // presence also satisfies the Tenancy prod-boot pre-flight (ARCH-0099 §1) — Reference = Intent.
        services.AddSingleton<ITenantResolver, ClaimTenantResolver>();
        services.AddSingleton<ITenantResolver, HeaderTenantResolver>();
        services.AddSingleton<ITenantResolver, SubdomainTenantResolver>();
        services.AddSingleton<ITenantResolver, PathTenantResolver>();

        // P4 — the middleware that scopes the request to the resolved, membership-authorized tenant (AfterAuthentication).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TenantResolutionContributor>());

        // P4 — invite-binds-to-identity + atomic verifiable deprovisioning.
        services.TryAddSingleton<InviteAcceptanceService>();
        services.TryAddSingleton<DeprovisioningService>();
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        // The Tenancy prod-boot pre-flight (ARCH-0099 §1) is satisfied by mere resolver PRESENCE — and this module
        // always registers all four. So a deployment that intended subdomain routing but forgot to configure
        // BaseHosts would still boot green and then silently never scope. Make the effective carrier config visible at
        // boot so that misconfiguration is loud, not a support ticket.
        var opts = services.GetService<IOptions<TenancyResolutionOptions>>()?.Value ?? new TenancyResolutionOptions();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Identity.Tenancy");

        var subdomainLive = opts.BaseHosts.Any(h => !string.IsNullOrWhiteSpace(h));
        logger?.LogInformation(
            "Koan.Identity.Tenancy carriers: claim='{Claim}', header='{Header}', path='{Path}', subdomain={Subdomain}, requireMembership={RequireMembership}.",
            opts.ClaimType, opts.HeaderName, opts.PathPrefix,
            subdomainLive ? $"baseHosts=[{string.Join(",", opts.BaseHosts)}]" : "INERT (no BaseHosts)",
            opts.RequireMembership);

        if (!subdomainLive)
            logger?.LogInformation(
                "Koan.Identity.Tenancy: the subdomain carrier is inert (no Koan:Data:Tenancy:Resolution:BaseHosts configured) — " +
                "subdomain-routed deployments must set BaseHosts or every request resolves no tenant.");

        if (!opts.RequireMembership)
            logger?.LogWarning(
                "Koan.Identity.Tenancy: RequireMembership=false DISABLES membership-authorization for ALL carriers, " +
                "including the forgeable header/subdomain/path — a client-asserted tenant is scoped in without an " +
                "authorization check. Leave it on in production (SEC-0007 P4).");

        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("Identity×Tenancy", b => b.Value(
            "membership-scoped access + tenant-resolution carriers (claim/header/subdomain/path) + " +
            "invite-binds-to-identity + atomic verifiable deprovisioning (SEC-0007 P4)"));
    }
}
