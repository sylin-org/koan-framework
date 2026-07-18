using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Audit;
using Koan.Identity.Impersonation;
using Koan.Identity.Management;
using Koan.Identity.Web;
using Koan.Tenancy;
using Koan.Web.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P3-core / Layer-3 acceptance (ARCH-0079 — real <c>AddKoan()</c>, offline): safe impersonation
/// (dual-control, time-boxed, actor-claim not a sub-swap, dangerous verbs 403, banner), JIT time-boxed grants,
/// and tamper-evident audit. (Local-credential/passkey/MFA/recovery — group 4 — is scoped separately.)
/// </summary>
[Collection("identity")]
public sealed class IdentityDayTwoSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public IdentityDayTwoSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    [Fact]
    public async Task Impersonation_requires_dual_control_and_is_time_boxed()
    {
        var svc = _fx.Services.GetRequiredService<ImpersonationService>();
        await new Identity { Id = "imp-actor", DisplayName = "Actor" }.Save();
        await new Identity { Id = "imp-target", DisplayName = "Target" }.Save();

        var grant = await svc.RequestAsync("imp-actor", "imp-target", "support ticket #42", "T-42");
        grant.IsApproved.Should().BeFalse("a request is pending until approved");
        (await svc.IsActiveAsync("imp-actor", "imp-target")).Should().BeFalse("a pending request is not active");

        // No self-approval — the approver must differ from the requesting actor.
        var selfApprove = async () => await svc.ApproveAsync(grant.Id, "imp-actor", TimeSpan.FromMinutes(15));
        await selfApprove.Should().ThrowAsync<InvalidOperationException>();

        await svc.ApproveAsync(grant.Id, "imp-approver", TimeSpan.FromMinutes(15));
        (await svc.IsActiveAsync("imp-actor", "imp-target")).Should().BeTrue("a different approver activates it, time-boxed");

        (await svc.RevokeAsync(grant.Id)).Should().BeTrue();
        (await svc.IsActiveAsync("imp-actor", "imp-target")).Should().BeFalse("the target/admin can revoke");
    }

    [Fact]
    public void Impersonated_principal_carries_actor_not_a_sub_swap()
    {
        var principal = new ClaimsPrincipal(ImpersonationClaims.BuildImpersonatedIdentity("the-target", "the-actor", new[] { "koan:reader" }));

        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("the-target", "the session acts as the target");
        ImpersonationClaims.IsImpersonating(principal).Should().BeTrue();
        ImpersonationClaims.ActorOf(principal).Should().Be("the-actor", "attribution to the real operator is preserved (never a sub-swap)");
    }

    [Fact]
    public void Dangerous_verbs_are_blocked_while_impersonating_only()
    {
        var impersonating = new ClaimsPrincipal(ImpersonationClaims.BuildImpersonatedIdentity("t", "a", Array.Empty<string>()));
        var normal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "t") }, "x"));

        ImpersonationGuard.IsBlocked(impersonating, "identity.delete").Should().BeTrue();
        ImpersonationGuard.IsBlocked(impersonating, "profile.read").Should().BeFalse("safe verbs are allowed while impersonating");
        ImpersonationGuard.IsBlocked(normal, "identity.delete").Should().BeFalse("a normal session can perform its own dangerous verbs");
    }

    [Fact]
    public void Banner_filter_advertises_active_impersonation()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(ImpersonationClaims.BuildImpersonatedIdentity("t", "the-actor", Array.Empty<string>())) };
        var ctx = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        new ImpersonationBannerFilter().OnActionExecuting(ctx);

        http.Response.Headers[ImpersonationClaims.BannerHeader].ToString().Should().Be("the-actor", "the banner advertises who is really acting");
    }

    [Fact]
    public async Task Jit_grant_expires_extends_and_revokes()
    {
        var jit = _fx.Services.GetRequiredService<JitGrantService>();
        using (Tenant.Use("studio-jit"))
        {
            var grant = await jit.GrantSupportAccessAsync("support-agent", "Orders", TimeSpan.FromMinutes(-1)); // already past
            grant.IsActive(DateTimeOffset.UtcNow).Should().BeFalse("a past TTL is immediately inactive (no standing access)");

            await jit.ExtendAsync(grant.Id, TimeSpan.FromHours(1));
            (await AgentGrant.Get(grant.Id))!.IsActive(DateTimeOffset.UtcNow).Should().BeTrue("one-click extend reactivates it");

            (await jit.RevokeAsync(grant.Id)).Should().BeTrue();
            (await AgentGrant.Get(grant.Id)).Should().BeNull("revoke removes the grant (fresh-per-request enforcement)");
        }
    }

    [Fact]
    public async Task Tamper_evident_audit_chain_detects_alteration_and_removal()
    {
        var chain = _fx.Services.GetRequiredService<AuditChain>();

        // Append (stamp + persist atomically) a short chain.
        var events = new List<AuditEvent>();
        for (var i = 0; i < 3; i++)
        {
            var e = new AuditEvent { Action = $"chain.test.{i}", Subject = "chain-subject", Target = $"X/{i}" };
            await chain.AppendAsync(e, ev => ev.Save());
            events.Add(e);
        }
        (await chain.VerifyAsync()).Intact.Should().BeTrue("a freshly-chained sequence verifies");

        // Alter a past event's content → content-hash break, pinpointed.
        events[1].After = "TAMPERED";
        await events[1].Save();
        var altered = await chain.VerifyAsync();
        altered.Intact.Should().BeFalse("altering a chained event breaks the hash chain");
        altered.EventsOrBrokenAt.Should().Be(events[1].Sequence, "the break is pinpointed to the tampered event");

        // Restore the content → verifies again (only Hash/content matter, not when the row was touched).
        events[1].After = null;
        await events[1].Save();
        (await chain.VerifyAsync()).Intact.Should().BeTrue("restoring the content re-verifies");

        // Remove a past event → sequence gap.
        await events[1].Remove();
        var removed = await chain.VerifyAsync();
        removed.Intact.Should().BeFalse("removing a chained event breaks the sequence");
        removed.Detail.Should().Contain("sequence gap", "the chain detects a deletion, not just an alteration");
    }

    [Fact]
    public async Task Build_session_is_fail_closed_without_an_active_grant()
    {
        var svc = _fx.Services.GetRequiredService<ImpersonationService>();
        await new Identity { Id = "bs-actor", DisplayName = "A" }.Save();
        await new Identity { Id = "bs-target", DisplayName = "T" }.Save();

        (await svc.BuildSessionAsync("bs-actor", "bs-target", Array.Empty<string>()))
            .Should().BeNull("no active grant → no impersonated principal (the session bridge fail-closes)");

        var grant = await svc.RequestAsync("bs-actor", "bs-target", "reason", null);
        await svc.ApproveAsync(grant.Id, "bs-approver", TimeSpan.FromMinutes(10));

        var principal = await svc.BuildSessionAsync("bs-actor", "bs-target", new[] { "koan:reader" });
        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("bs-target");
        ImpersonationClaims.ActorOf(principal).Should().Be("bs-actor");
        principal.IsInRole("koan:reader").Should().BeTrue("the impersonated session carries the target's roles");
    }

    [Fact]
    public async Task Destructive_admin_verbs_are_403_while_impersonating_even_with_operator_role()
    {
        // Worst case: impersonating a TARGET who holds the operator role — [Authorize(Roles=Operator)] would pass,
        // so the guard (not the role gate) must be what blocks the destructive verb.
        var impersonatingOperator = new ClaimsPrincipal(
            ImpersonationClaims.BuildImpersonatedIdentity("imp-t3", "imp-op3", new[] { IdentityRoles.Operator }));

        var admin = new IdentityAdminController(_fx.Services.GetRequiredService<IdentityLifecycleService>());
        admin.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = impersonatingOperator } };
        ((await admin.Delete("anyone", default)).Result as ObjectResult)?.StatusCode.Should().Be(403, "delete is guarded, not just role-gated");

        using var scope = _fx.Services.CreateScope();
        var access = new IdentityAccessController(
            scope.ServiceProvider.GetRequiredService<Koan.Identity.Access.EffectiveAccessResolver>(),
            scope.ServiceProvider.GetRequiredService<Koan.Identity.Access.AccessExplainer>(),
            _fx.Services.GetRequiredService<IdentityRoleService>());
        access.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = impersonatingOperator } };
        ((await access.Grant("anyone", new IdentityAccessController.GrantRequest("koan:admin"), default)).Result as ObjectResult)?.StatusCode.Should().Be(403, "role grant is guarded while impersonating");
    }

    [Fact]
    public async Task Audit_attributes_the_acting_operator()
    {
        var http = _fx.Services.GetRequiredService<IHttpContextAccessor>();
        var prior = http.HttpContext;
        try
        {
            http.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "the-operator") }, "x")),
            };
            await new Identity { Id = "attributed-subject", DisplayName = "Attr" }.Save();

            (await AuditEvent.Query(a => a.Subject == "attributed-subject"))
                .Should().Contain(a => a.Actor == "the-operator", "audit records WHO performed the mutation (not null)");
        }
        finally
        {
            http.HttpContext = prior;
        }
    }
}
