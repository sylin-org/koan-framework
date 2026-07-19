using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Tenancy;
using Koan.Identity.Tenancy.Initialization;
using Koan.Identity.Tenancy.Resolvers;
using Koan.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Web.Context;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P4 / Layer-4 acceptance (ARCH-0079 — real <c>AddKoan()</c>, offline): the tenant-resolution carriers and
/// the ordered Web context contributor that scopes a request to a <b>membership-authorized</b> tenant. The
/// scoping is enforced on the REQUEST PATH (re-evaluated every request) — a forged carrier or a non-member is never
/// scoped in, which is the whole point of the SnapVault D1 gap this closes.
/// </summary>
[Collection("identity")]
public sealed class TenantResolutionSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public TenantResolutionSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    // ── The pure carrier extractors (no host needed) ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("acme.app.example.com", "app.example.com", "acme")]
    [InlineData("APP.example.com", "app.example.com", null)]          // the bare base host is no tenant
    [InlineData("a.b.app.example.com", "app.example.com", null)]      // only a single leading label is a code
    [InlineData("acme.other.com", "app.example.com", null)]           // not under the base host
    public void Subdomain_carrier_extracts_a_single_leading_label(string host, string baseHost, string? expected)
        => SubdomainTenantResolver.ExtractCode(host, new[] { baseHost }).Should().Be(expected);

    [Theory]
    [InlineData("/t/acme/orders", "/t/", "acme")]
    [InlineData("/t/acme", "/t/", "acme")]
    [InlineData("/t/", "/t/", null)]
    [InlineData("/orders", "/t/", null)]
    public void Path_carrier_extracts_the_segment_after_the_prefix(string path, string prefix, string? expected)
        => PathTenantResolver.ExtractCode(path, prefix).Should().Be(expected);

    // ── The contributor, end-to-end against the real resolvers + store ──────────────────────────────────────────

    private async Task RunContributorAsync(DefaultHttpContext context, Func<DefaultHttpContext, Task> inside)
    {
        var contributor = new TenantResolutionContributor(
            _fx.Services.GetServices<ITenantResolver>(),
            _fx.Services.GetRequiredService<IOptions<TenancyResolutionOptions>>());
        var webContext = new WebContext(context);
        await contributor.ContributeAsync(webContext);
        using var entered = webContext.EnterPending();
        await inside(context);
    }

    private async Task<string?> RunContextAsync(Action<DefaultHttpContext> configure)
    {
        var ctx = new DefaultHttpContext { RequestServices = _fx.Services };
        configure(ctx);
        string? scoped = null;
        await RunContributorAsync(ctx, _ =>
        {
            scoped = Tenant.Current?.Id;
            return Task.CompletedTask;
        });
        return scoped;
    }

    private static void SignedIn(DefaultHttpContext ctx, string subject)
        => ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, subject) }, "test"));

    [Theory]
    [InlineData("HeaderName", " ")]
    [InlineData("PathPrefix", "relative")]
    public void Invalid_carrier_configuration_fails_standard_options_validation(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TenancyResolutionOptions.SectionPath}:{key}"] = value,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        new IdentityTenancyModule().Register(services);
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<TenancyResolutionOptions>>().Value;

        read.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task Header_carrier_scopes_a_member_by_routing_code()
    {
        await new Identity { Id = "carrier-alice" }.Save();
        await new TenantRecord { Id = "tr-acme", Name = "Acme", Code = "acme" }.Save();
        await new Membership { TenantId = "tr-acme", IdentityId = "carrier-alice", Roles = { "koan:member" } }.Save();

        var scoped = await RunContextAsync(ctx =>
        {
            SignedIn(ctx, "carrier-alice");
            ctx.Request.Headers["X-Koan-Tenant"] = "acme"; // the Code, not the id
        });

        scoped.Should().Be("tr-acme", "the header code resolves to the tenant id and the subject is a member");
    }

    [Fact]
    public async Task Path_carrier_scopes_a_member()
    {
        await new Identity { Id = "carrier-bob" }.Save();
        await new TenantRecord { Id = "tr-globex", Name = "Globex", Code = "globex" }.Save();
        await new Membership { TenantId = "tr-globex", IdentityId = "carrier-bob", Roles = { "koan:member" } }.Save();

        var scoped = await RunContextAsync(ctx =>
        {
            SignedIn(ctx, "carrier-bob");
            ctx.Request.Path = "/t/globex/orders";
        });

        scoped.Should().Be("tr-globex");
    }

    [Fact]
    public async Task Claim_carrier_scopes_a_member_by_tenant_id()
    {
        await new Identity { Id = "carrier-claire" }.Save();
        await new TenantRecord { Id = "tr-initech", Name = "Initech" }.Save();
        await new Membership { TenantId = "tr-initech", IdentityId = "carrier-claire", Roles = { "koan:member" } }.Save();

        var scoped = await RunContextAsync(ctx =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "carrier-claire"),
                new Claim("tenant", "tr-initech"),
            }, "test"));
        });

        scoped.Should().Be("tr-initech");
    }

    [Fact]
    public async Task A_forged_carrier_never_scopes_a_NON_member_in()
    {
        await new TenantRecord { Id = "tr-secret", Name = "Secret", Code = "secret" }.Save();
        // mallory is signed in but holds NO membership in tr-secret.

        var scoped = await RunContextAsync(ctx =>
        {
            SignedIn(ctx, "mallory");
            ctx.Request.Headers["X-Koan-Tenant"] = "secret";
        });

        scoped.Should().BeNull("membership-authorization is load-bearing — a forged header cannot scope a non-member in");
    }

    [Fact]
    public async Task An_anonymous_request_is_never_scoped()
    {
        await new TenantRecord { Id = "tr-anon", Name = "Anon", Code = "anon" }.Save();

        var scoped = await RunContextAsync(ctx => ctx.Request.Headers["X-Koan-Tenant"] = "anon");

        scoped.Should().BeNull("an anonymous principal can never be a member, so it is never scoped");
    }

    [Fact]
    public async Task An_unknown_routing_code_resolves_to_nothing()
    {
        var scoped = await RunContextAsync(ctx =>
        {
            SignedIn(ctx, "carrier-alice");
            ctx.Request.Headers["X-Koan-Tenant"] = "no-such-tenant-code";
        });

        scoped.Should().BeNull("an unrecognized code/id resolves to no tenant");
    }

    [Fact]
    public async Task A_members_tenant_roles_are_projected_onto_the_request_principal()
    {
        await new Identity { Id = "roleproj-user" }.Save();
        await new TenantRecord { Id = "tr-roleproj", Name = "RoleProj", Code = "roleproj" }.Save();
        await new Membership { TenantId = "tr-roleproj", IdentityId = "roleproj-user", Roles = { "koan:manager" } }.Save();

        bool inRoleInside = false;
        var ctx = new DefaultHttpContext { RequestServices = _fx.Services };
        SignedIn(ctx, "roleproj-user");
        ctx.Request.Headers["X-Koan-Tenant"] = "roleproj";
        await RunContributorAsync(ctx, c =>
        {
            inRoleInside = c.User.IsInRole("koan:manager");
            return Task.CompletedTask;
        });

        inRoleInside.Should().BeTrue("Membership.Roles must be HONORED on the request path (projected as role claims the authorize floor reads), not write-only");
        ctx.User.IsInRole("koan:manager").Should().BeFalse("the projection is scoped to the request — the principal is restored after the scope");
    }

    [Fact]
    public async Task A_reserved_host_role_on_a_membership_is_never_projected()
    {
        await new Identity { Id = "backdoor-user" }.Save();
        await new TenantRecord { Id = "tr-backdoor", Name = "Backdoor", Code = "backdoor" }.Save();
        // A membership that somehow carries the HOST operator role (e.g. a bad seed/import/direct save) alongside a
        // legit tenant role — the projection must strip the host role (ARCH-0104 "no master backdoor").
        await new Membership
        {
            TenantId = "tr-backdoor",
            IdentityId = "backdoor-user",
            Roles = { TenancyRoles.Operator, IdentityRoles.Operator, "koan:member" }
        }.Save();

        bool tenancyOperatorInside = true, identityOperatorInside = true, memberInside = false;
        var ctx = new DefaultHttpContext { RequestServices = _fx.Services };
        SignedIn(ctx, "backdoor-user");
        ctx.Request.Headers["X-Koan-Tenant"] = "backdoor";
        await RunContributorAsync(ctx, c =>
        {
            tenancyOperatorInside = c.User.IsInRole(TenancyRoles.Operator);
            identityOperatorInside = c.User.IsInRole(IdentityRoles.Operator);
            memberInside = c.User.IsInRole("koan:member");
            return Task.CompletedTask;
        });

        tenancyOperatorInside.Should().BeFalse("a tenant membership must never project fleet operator authority");
        identityOperatorInside.Should().BeFalse("a tenant membership must never project global identity authority");
        memberInside.Should().BeTrue("legit tenant roles still project");
    }

    [Fact]
    public async Task A_non_member_gets_no_projected_roles()
    {
        await new TenantRecord { Id = "tr-noproj", Name = "NoProj", Code = "noproj" }.Save();
        await new Membership { TenantId = "tr-noproj", IdentityId = "someone-else", Roles = { "koan:manager" } }.Save();

        bool? scoped = null; bool inRole = true;
        var ctx = new DefaultHttpContext { RequestServices = _fx.Services };
        SignedIn(ctx, "outsider");
        ctx.Request.Headers["X-Koan-Tenant"] = "noproj";
        await RunContributorAsync(ctx, c =>
        {
            scoped = Tenant.Current is not null;
            inRole = c.User.IsInRole("koan:manager");
            return Task.CompletedTask;
        });

        scoped.Should().Be(false, "a non-member is not scoped in");
        inRole.Should().BeFalse("and never inherits another member's tenant role");
    }

    [Fact]
    public async Task A_removed_membership_stops_scoping_at_the_very_next_request()
    {
        await new Identity { Id = "churn-user" }.Save();
        await new TenantRecord { Id = "tr-churn", Name = "Churn", Code = "churn" }.Save();
        var membership = await new Membership { TenantId = "tr-churn", IdentityId = "churn-user", Roles = { "koan:member" } }.Save();

        // Request 1 — scoped while a member.
        (await RunContextAsync(ctx => { SignedIn(ctx, "churn-user"); ctx.Request.Headers["X-Koan-Tenant"] = "churn"; }))
            .Should().Be("tr-churn");

        await membership.Remove();

        // Request 2 — the SAME carrier no longer scopes, because authorization is re-evaluated on the request path.
        (await RunContextAsync(ctx => { SignedIn(ctx, "churn-user"); ctx.Request.Headers["X-Koan-Tenant"] = "churn"; }))
            .Should().BeNull("enforcement is on the request path, not a write-only flag");
    }

    [Fact]
    public async Task A_deactivated_member_is_never_scoped()
    {
        await new Identity { Id = "inactive-user", Status = IdentityStatus.Deactivated }.Save();
        await new TenantRecord { Id = "tr-inactive", Name = "Inactive", Code = "inactive" }.Save();
        await new Membership { TenantId = "tr-inactive", IdentityId = "inactive-user", Roles = { "koan:member" } }.Save();

        var scoped = await RunContextAsync(ctx =>
        {
            SignedIn(ctx, "inactive-user");
            ctx.Request.Headers["X-Koan-Tenant"] = "inactive";
        });

        scoped.Should().BeNull(
            "a stale membership or bearer principal cannot restore tenant scope for a deactivated durable person");
    }
}
