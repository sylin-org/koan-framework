using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Access;
using Koan.Identity.Management;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P2/P4 — the Membership effective-access contributor (the one the Layer-2 resolver expected, shipping only
/// with the Identity × Tenancy bridge). It lights up membership roles over the SAME resolver/explainer as the global
/// and grant contributors — no code-path fork — and degrades to empty in the global (no-tenant) view.
/// </summary>
[Collection("identity")]
public sealed class IdentityTenancyAccessSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public IdentityTenancyAccessSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    private IdentityRoleService Roles => _fx.Services.GetRequiredService<IdentityRoleService>();
    private EffectiveAccessResolver NewResolver(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<EffectiveAccessResolver>();

    [Fact]
    public async Task Membership_roles_light_up_inside_the_tenant_scope_over_the_same_resolver()
    {
        const string id = "ms-eff";
        await new Identity { Id = id, DisplayName = "MS Eff" }.Save();
        await Roles.GrantAsync(id, "koan:editor"); // global role
        await new Membership { TenantId = "ms-studio", IdentityId = id, Roles = { "koan:manager" } }.Save();

        using (Tenant.Use("ms-studio"))
        {
            using var scope = _fx.Services.CreateScope();
            var access = await NewResolver(scope).ResolveAsync(id);

            access.Roles.Should().Contain("koan:editor", "the global-role contributor still answers");
            access.Roles.Should().Contain("koan:manager", "the Membership contributor supplies the tenant role");
            access.Facts.Should().Contain(f => f.Source == "Membership" && f.Value == "koan:manager" && f.Scope == "ms-studio");
        }
    }

    [Fact]
    public async Task Membership_contributor_degrades_to_empty_in_the_global_no_tenant_view()
    {
        const string id = "ms-global";
        await new Identity { Id = id, DisplayName = "MS Global" }.Save();
        await new Membership { TenantId = "ms-studio-2", IdentityId = id, Roles = { "koan:manager" } }.Save();

        using var scope = _fx.Services.CreateScope();
        var access = await NewResolver(scope).ResolveAsync(id); // NO tenant scope

        access.Facts.Should().NotContain(f => f.Source == "Membership",
            "with no tenant in scope there is no membership plane — the contributor degrades to empty (mirrors AgentGrant)");
    }

    [Fact]
    public async Task A_role_held_globally_AND_via_membership_is_flagged_as_an_overlap()
    {
        const string id = "ms-overlap";
        await new Identity { Id = id, DisplayName = "MS Overlap" }.Save();
        await Roles.GrantAsync(id, "koan:admin"); // global
        await new Membership { TenantId = "ms-studio-3", IdentityId = id, Roles = { "koan:admin" } }.Save(); // and per-tenant

        using (Tenant.Use("ms-studio-3"))
        {
            using var scope = _fx.Services.CreateScope();
            var access = await NewResolver(scope).ResolveAsync(id);

            access.OverlappingRoles.Should().Contain("koan:admin",
                "the same role from two sources (global + membership) is the role-explosion early warning");
            access.Roles.Should().Contain("koan:admin").And.HaveCount(1, "the effective view dedupes the role itself");
        }
    }

    [Fact]
    public async Task Membership_roles_are_scoped_to_the_ambient_tenant_only()
    {
        const string id = "ms-scope";
        await new Identity { Id = id, DisplayName = "MS Scope" }.Save();
        await new Membership { TenantId = "ms-studio-a", IdentityId = id, Roles = { "koan:a-role" } }.Save();
        await new Membership { TenantId = "ms-studio-b", IdentityId = id, Roles = { "koan:b-role" } }.Save();

        using (Tenant.Use("ms-studio-a"))
        {
            using var scope = _fx.Services.CreateScope();
            var access = await NewResolver(scope).ResolveAsync(id);
            access.Roles.Should().Contain("koan:a-role");
            access.Roles.Should().NotContain("koan:b-role", "only the AMBIENT tenant's membership roles are contributed");
        }
    }
}
