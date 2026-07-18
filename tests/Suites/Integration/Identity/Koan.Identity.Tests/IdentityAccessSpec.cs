using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Access;
using Koan.Identity.Management;
using Koan.Tenancy;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Koan.Web.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P2 / Layer-2 acceptance (ARCH-0079 — real <c>AddKoan()</c>, offline): the global role binding, the
/// contributor-pipeline effective-access resolver, role-overlap detection, and the bidirectional explainer
/// (reverse "why" + one-click revoke; forward "can" through the real authorize engine).
/// </summary>
[Collection("identity")]
public sealed class IdentityAccessSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public IdentityAccessSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    private IdentityRoleService Roles => _fx.Services.GetRequiredService<IdentityRoleService>();

    private EffectiveAccessResolver NewResolver(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<EffectiveAccessResolver>();
    private AccessExplainer NewExplainer(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AccessExplainer>();

    [Fact]
    public async Task Global_role_grant_is_idempotent_and_listable()
    {
        const string id = "role-user";
        await new Identity { Id = id, DisplayName = "Role User" }.Save();

        await Roles.GrantAsync(id, "koan:admin");
        await Roles.GrantAsync(id, "koan:admin"); // re-grant: deterministic id → one row

        (await Roles.ListAsync(id)).Should().ContainSingle().Which.RoleKey.Should().Be("koan:admin");

        (await Roles.RevokeAsync(id, "koan:admin")).Should().BeTrue();
        (await Roles.ListAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Effective_access_flattens_roles_and_capabilities_across_contributors()
    {
        const string id = "eff-user";
        await new Identity { Id = id, DisplayName = "Eff" }.Save();
        await Roles.GrantAsync(id, "koan:editor"); // global role — IAmbientExempt, no tenant needed

        // AgentGrant is tenant-scoped; create + resolve inside a tenant scope so the grant contributor lights up.
        using (Tenant.Use("studio-x"))
        {
            await new AgentGrant { Subject = id, Capability = "has:scope:orders", Resource = "Orders" }.Save();

            using var scope = _fx.Services.CreateScope();
            var access = await NewResolver(scope).ResolveAsync(id);

            access.Roles.Should().Contain("koan:editor", "the IdentityRole contributor supplies global roles");
            access.Capabilities.Should().Contain("has:scope:orders", "the AgentGrant contributor supplies tenant capabilities");
            access.Facts.Should().Contain(f => f.Source == "IdentityRole" && f.Value == "koan:editor");
            access.Facts.Should().Contain(f => f.Source == "AgentGrant" && f.Resource == "Orders");
        }
    }

    [Fact]
    public async Task Reverse_explainer_names_contributing_rows_and_revoke_removes_access()
    {
        const string id = "why-user";
        await new Identity { Id = id, DisplayName = "Why" }.Save();

        using (Tenant.Use("studio-y"))
        {
            await new AgentGrant { Subject = id, Capability = "has:scope:orders:fulfill", Resource = "Orders" }.Save();

            using var scope = _fx.Services.CreateScope();
            var explainer = NewExplainer(scope);

            var why = await explainer.WhyAsync(id, "Orders");
            var grantFact = why.SingleOrDefault(f => f.Source == "AgentGrant" && f.Resource == "Orders");
            grantFact.Should().NotBeNull("'why does X have access to Orders' names the exact contributing grant row");

            (await explainer.RevokeAsync(new AccessFactRef(grantFact!.RowType, grantFact.RowId))).Should().BeTrue();
            (await explainer.WhyAsync(id, "Orders")).Should().NotContain(f => f.RowId == grantFact.RowId, "revoke removed the row");
        }
    }

    [Fact]
    public async Task Forward_explainer_runs_the_real_authorize_engine_with_contributing_facts()
    {
        const string id = "can-user";
        await new Identity { Id = id, DisplayName = "Can" }.Save();
        await Roles.GrantAsync(id, "koan:reader");

        using var scope = _fx.Services.CreateScope();
        var decision = await NewExplainer(scope).CanAsync(id, "read", "Orders");

        decision.Reason.Should().NotBe("no authorize engine is registered",
            "the real IAuthorize engine must be wired so preview == production");
        decision.ContributingFacts.Should().Contain(f => f.Value == "koan:reader", "the decision is explained by the contributing facts");
    }

    [Fact]
    public async Task Resolver_detects_role_overlap_across_sources()
    {
        // Two sources confer the same role (e.g. a global IdentityRole + a per-tenant Membership) — the role-explosion
        // early warning. Driven with stand-in contributors so the detector is proven without needing Koan.Tenancy.
        var resolver = new EffectiveAccessResolver(new IEffectiveAccessContributor[]
        {
            new StubContributor { Facts = new[] { new AccessFact("IdentityRole", "role", "koan:admin", "*", "global", "IdentityRole", "r1", null) } },
            new StubContributor { Facts = new[] { new AccessFact("Membership", "role", "koan:admin", "*", "tenant-a", "Membership", "m1", null) } },
        });

        var access = await resolver.ResolveAsync("x");
        access.OverlappingRoles.Should().Contain("koan:admin");
        access.Roles.Should().ContainSingle().Which.Should().Be("koan:admin", "the effective view dedupes");
    }

    [Fact]
    public async Task Global_role_is_stamped_onto_the_sign_in_principal_so_production_honors_it()
    {
        const string id = "global-role-user";
        await new Identity { Id = id, DisplayName = "GR" }.Save();
        await Roles.GrantAsync(id, "koan:admin");

        using var scope = _fx.Services.CreateScope();
        var ci = new ClaimsIdentity("test");
        ci.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
        var ctx = new AuthSignInContext
        {
            Provider = "test",
            Identity = ci,
            Services = scope.ServiceProvider,
            HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
        };
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchSignIn(ctx, default);

        ci.HasClaim(ClaimTypes.Role, "koan:admin").Should()
            .BeTrue("a global IdentityRole must be stamped onto the cookie so the authorize floor actually honors it");
    }

    [Fact]
    public async Task Expired_agent_grants_are_excluded_from_the_access_preview()
    {
        const string id = "expiry-user";
        await new Identity { Id = id, DisplayName = "Exp" }.Save();

        using (Tenant.Use("studio-z"))
        {
            await new AgentGrant { Subject = id, Capability = "has:scope:live", Resource = "Orders", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60) }.Save();
            await new AgentGrant { Subject = id, Capability = "has:scope:expired", Resource = "Orders", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }.Save();

            using var scope = _fx.Services.CreateScope();
            var access = await NewResolver(scope).ResolveAsync(id);

            access.Capabilities.Should().Contain("has:scope:live");
            access.Capabilities.Should().NotContain("has:scope:expired", "an expired grant must not show as live access (matches the production gate)");
        }
    }

    [Fact]
    public async Task Global_role_grant_and_revoke_self_audit()
    {
        const string id = "role-audit-user";
        await new Identity { Id = id, DisplayName = "Role Audit" }.Save();
        var role = await Roles.GrantAsync(id, "koan:admin");

        (await AuditEvent.Query(a => a.Subject == id)).Should().Contain(
            a => a.Action == "identityrole.created" && a.Target == $"IdentityRole/{role.Id}",
            "granting a global role self-audits with the person as Subject");

        (await Roles.RevokeAsync(id, "koan:admin")).Should().BeTrue();
        (await AuditEvent.Query(a => a.Subject == id)).Should().Contain(
            a => a.Action == "identityrole.deleted", "revoking a global role self-audits the deletion");
    }

    [Fact]
    public async Task Why_includes_global_role_for_any_resource_and_revoke_drives_the_role_branch()
    {
        const string id = "why-role-user";
        await new Identity { Id = id, DisplayName = "WhyRole" }.Save();
        await Roles.GrantAsync(id, "koan:admin");

        using var scope = _fx.Services.CreateScope();
        var explainer = NewExplainer(scope);

        var roleFact = (await explainer.WhyAsync(id, "Invoices")).SingleOrDefault(f => f.Source == "IdentityRole" && f.Value == "koan:admin");
        roleFact.Should().NotBeNull("a global role (Resource=*) contributes to access on ANY resource");

        (await explainer.RevokeAsync(new AccessFactRef(roleFact!.RowType, roleFact.RowId))).Should().BeTrue("drives the IdentityRole revoke branch");
        (await explainer.RevokeAsync(new AccessFactRef("Membership", "no-such"))).Should().BeFalse("an unknown row type fails closed");
        (await explainer.WhyAsync(id, "Invoices")).Should().NotContain(f => f.RowId == roleFact.RowId);
    }

    [Fact]
    public async Task Resolver_reports_no_overlap_for_distinct_roles()
    {
        var resolver = new EffectiveAccessResolver(new IEffectiveAccessContributor[]
        {
            new StubContributor { Facts = new[] { new AccessFact("IdentityRole", "role", "koan:a", "*", "global", "IdentityRole", "r1", null) } },
            new StubContributor { Facts = new[] { new AccessFact("IdentityRole", "role", "koan:b", "*", "global", "IdentityRole", "r2", null) } },
        });
        (await resolver.ResolveAsync("x")).OverlappingRoles.Should().BeEmpty("distinct roles are not overlaps");
    }

    // Parameterless + init-only so the [KoanDiscoverable] scan that auto-registers it can construct it harmlessly
    // (empty facts); the overlap test news it up directly with facts.
    private sealed class StubContributor : IEffectiveAccessContributor
    {
        public AccessFact[] Facts { get; init; } = Array.Empty<AccessFact>();
        public Task<IReadOnlyList<AccessFact>> ContributeAsync(string identityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccessFact>>(Facts);
    }
}
