using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Identity.Tenancy.Deprovisioning;
using Koan.Identity.Tenancy.Resolvers;
using Koan.Tenancy;
using Koan.Web.Hosting;

namespace Koan.Identity.Tenancy.Initialization;

/// <summary>
/// SEC-0007 Layer 4 — Reference = Intent: referencing <c>Koan.Identity.Tenancy</c> composes the durable person with
/// the tenancy control plane. It lights up membership-scoped effective access (the Membership contributor over the
/// SAME Layer-2 resolver), the four tenant-resolution carriers + the <c>AfterAuthentication</c> middleware that
/// scopes the request to an active membership-authorized tenant, and lifecycle closure with integrity-checked
/// receipts. Project dependencies express availability; registrations are order-independent.
/// </summary>
public sealed class IdentityTenancyModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TenancyResolutionOptions>(TenancyResolutionOptions.SectionPath)
            .Validate(
                static value => !string.IsNullOrWhiteSpace(value.ClaimType),
                $"{nameof(TenancyResolutionOptions.ClaimType)} cannot be empty.")
            .Validate(
                static value => !string.IsNullOrWhiteSpace(value.HeaderName),
                $"{nameof(TenancyResolutionOptions.HeaderName)} cannot be empty.")
            .Validate(
                static value => !string.IsNullOrWhiteSpace(value.PathPrefix) && value.PathPrefix.StartsWith('/'),
                $"{nameof(TenancyResolutionOptions.PathPrefix)} must be a non-empty absolute path prefix.")
            .Validate(
                static value => value.BaseHosts.All(host => !string.IsNullOrWhiteSpace(host)),
                $"{nameof(TenancyResolutionOptions.BaseHosts)} cannot contain empty host names.");

        // P4 — the four tenant-resolution carriers, registered in resolution order (claim → header → subdomain →
        // path). The middleware tries them in registration order and the first non-null candidate wins. Their mere
        // presence also satisfies the Tenancy prod-boot pre-flight (ARCH-0099 §1) — Reference = Intent.
        services.AddSingleton<ITenantResolver, ClaimTenantResolver>();
        services.AddSingleton<ITenantResolver, HeaderTenantResolver>();
        services.AddSingleton<ITenantResolver, SubdomainTenantResolver>();
        services.AddSingleton<ITenantResolver, PathTenantResolver>();

        // P4 — the middleware that scopes the request to the resolved, membership-authorized tenant (AfterAuthentication).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TenantResolutionContributor>());

        // P4 — one explicit lifecycle workflow for closing seats and durable persons.
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
            "Koan.Identity.Tenancy carriers: claim='{Claim}', header='{Header}', path='{Path}', subdomain={Subdomain}; every carrier requires an active durable member.",
            opts.ClaimType, opts.HeaderName, opts.PathPrefix,
            subdomainLive ? $"baseHosts=[{string.Join(",", opts.BaseHosts)}]" : "INERT (no BaseHosts)");

        if (!subdomainLive)
            logger?.LogInformation(
                "Koan.Identity.Tenancy: the subdomain carrier is inert (no Koan:Data:Tenancy:Resolution:BaseHosts configured) — " +
                "subdomain-routed deployments must set BaseHosts or every request resolves no tenant.");

        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("Identity×Tenancy", b => b.Value(
            "active-membership tenant scope + tenant-role projection + effective-access facts + " +
            "integrity-checked lifecycle receipts"));
    }
}
