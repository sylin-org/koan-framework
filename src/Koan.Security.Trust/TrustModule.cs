using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Security.Trust.Dev;
using Koan.Security.Trust.Inbound;
using Koan.Security.Trust.Issuer;

namespace Koan.Security.Trust;

/// <summary>
/// The workload-token and ambient-identity pillar (ARCH-0086 <see cref="KoanModule"/>).
/// <para>
/// This lower-level pillar owns one ES256 issuer, its <c>Koan.bearer</c> verifier, and ambient
/// <c>Identity</c>. Web Auth, Auth Server, and MCP consume it one-way; resource-specific audience and
/// authorization policy remain at their respective edges.
/// </para>
/// </summary>
public sealed class TrustModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TrustIssuerOptions>(TrustIssuerOptions.SectionPath);

        // One issuer, one algorithm, one key lifecycle seam. The default random per-process key is safe for
        // direct/single-host use. Auth Server replaces the store with its persisted rotating implementation
        // outside Development and owns the continuity guard for that larger promise.
        services.TryAddSingleton<IIssuerKeyStore, EphemeralIssuerKeyStore>();
        services.AddSingleton<IIssuer, EcdsaIssuer>();

        // Trust owns bearer verification, so referencing Trust + AddKoan is the complete activation surface.
        // This is additive: Web Auth may still establish its cookie scheme as the application default.
        services.AddAuthentication().AddKoanBearer();

        // Identity.Current reads HttpContext.User through this accessor (idempotent).
        services.AddHttpContextAccessor();

        // The parser belongs here; Web Auth's Development-only context contributor owns request activation.
        services.AddKoanOptions<DevIdentityOptions>(DevIdentityOptions.SectionPath);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Issuer: ES256; inbound scheme: " + KoanBearerDefaults.AuthenticationScheme);
    }
}
