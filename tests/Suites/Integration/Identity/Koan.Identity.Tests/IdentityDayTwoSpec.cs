using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Access;
using Koan.Identity.Audit;
using Koan.Identity.Impersonation;
using Koan.Identity.Management;
using Koan.Identity.Roles;
using Koan.Identity.Web;
using Koan.Tenancy;
using Koan.Web.Auth.Flow;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
/// and tamper-evident audit. Local credentials and application-managed factors are outside the supported V1 surface.
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
        var principal = new ClaimsPrincipal(ImpersonationClaims.BuildImpersonatedIdentity("the-target", "the-actor"));

        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("the-target", "the session acts as the target");
        ImpersonationClaims.IsImpersonating(principal).Should().BeTrue();
        ImpersonationClaims.ActorOf(principal).Should().Be("the-actor", "attribution to the real operator is preserved (never a sub-swap)");
    }

    [Fact]
    public void Dangerous_verbs_are_blocked_while_impersonating_only()
    {
        var impersonating = new ClaimsPrincipal(ImpersonationClaims.BuildImpersonatedIdentity("t", "a"));
        var normal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "t") }, "x"));

        ImpersonationGuard.IsBlocked(impersonating, "identity.delete").Should().BeTrue();
        ImpersonationGuard.IsBlocked(impersonating, "profile.read").Should().BeFalse("safe verbs are allowed while impersonating");
        ImpersonationGuard.IsBlocked(normal, "identity.delete").Should().BeFalse("a normal session can perform its own dangerous verbs");
    }

    [Fact]
    public void Banner_filter_advertises_active_impersonation()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(ImpersonationClaims.BuildImpersonatedIdentity("t", "the-actor")) };
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
            var before = DateTimeOffset.UtcNow;
            var grant = await jit.GrantSupportAccessAsync("support-agent", "Orders", TimeSpan.FromMinutes(-1)); // already past
            grant.IsActive(DateTimeOffset.UtcNow).Should().BeFalse("a past TTL is immediately inactive (no standing access)");
            grant.IssuedAt.Should().NotBeNull("a JIT grant records when it was issued");
            grant.IssuedAt!.Value.Should().BeOnOrAfter(before);
            grant.ClientId.Should().BeNull("a JIT grant is issued to no OAuth client");

            await jit.ExtendAsync(grant.Id, TimeSpan.FromHours(1));
            var extended = await AgentGrant.Get(grant.Id);
            extended!.IsActive(DateTimeOffset.UtcNow).Should().BeTrue("one-click extend reactivates it");
            extended.IssuedAt.Should().BeCloseTo(grant.IssuedAt.Value, TimeSpan.FromMilliseconds(1), "extending keeps when it was issued");

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

        // Restore the shared fixture's chain so this destructive proof cannot contaminate later acceptance facts.
        await events[1].Save();
        (await chain.VerifyAsync()).Intact.Should().BeTrue("restoring the removed event repairs the test fixture");
    }

    [Fact]
    public async Task Build_session_is_fail_closed_without_an_active_grant()
    {
        var svc = _fx.Services.GetRequiredService<ImpersonationService>();
        await new Identity { Id = "bs-actor", DisplayName = "A" }.Save();
        await new Identity { Id = "bs-target", DisplayName = "T" }.Save();

        (await svc.BuildSessionAsync("bs-actor", "bs-target"))
            .Should().BeNull("no active grant → no impersonated principal (the session bridge fail-closes)");

        var grant = await svc.RequestAsync("bs-actor", "bs-target", "reason", null);
        await svc.ApproveAsync(grant.Id, "bs-approver", TimeSpan.FromMinutes(10));

        var principal = await svc.BuildSessionAsync("bs-actor", "bs-target");
        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("bs-target");
        ImpersonationClaims.ActorOf(principal).Should().Be("bs-actor");
        principal.FindAll(ClaimTypes.Role).Should().BeEmpty("the target's roles are projected per request, like anyone's");
    }

    [Fact]
    public async Task Destructive_admin_verbs_are_403_while_impersonating_even_with_operator_role()
    {
        // Worst case: impersonating a TARGET who holds the operator role — [Authorize(Roles=Operator)] would pass,
        // so the guard (not the role gate) must be what blocks the destructive verb.
        var impersonatingOperator = WithRole(ImpersonationClaims.BuildImpersonatedIdentity("imp-t3", "imp-op3"), IdentityRoles.Operator);

        var admin = new IdentityAdminController(_fx.Services.GetRequiredService<IdentityLifecycleService>());
        admin.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = impersonatingOperator } };
        ((await admin.Delete("anyone", default)).Result as ObjectResult)?.StatusCode.Should().Be(403, "delete is guarded, not just role-gated");

        using var scope = _fx.Services.CreateScope();
        var access = new IdentityAccessController(
            scope.ServiceProvider.GetRequiredService<Koan.Identity.Access.EffectiveAccessResolver>(),
            scope.ServiceProvider.GetRequiredService<Koan.Identity.Access.AccessExplainer>(),
            _fx.Services.GetRequiredService<Koan.Identity.Roles.RoleCollection>());
        access.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = impersonatingOperator } };
        ((await access.Grant("anyone", new IdentityAccessController.GrantRequest("koan:admin"), default)).Result as ObjectResult)?.StatusCode.Should().Be(403, "role grant is guarded while impersonating");
    }

    [Fact]
    public async Task Simulated_principals_carry_exactly_the_role_claims_a_real_request_carries()
    {
        var roles = _fx.Services.GetRequiredService<RoleCollection>();
        var suffix = Guid.NewGuid().ToString("N");
        var target = $"parity-target:{suffix}";
        var actor = $"parity-actor:{suffix}";
        await new Identity { Id = target, DisplayName = "Parity target" }.Save();
        await new Identity { Id = actor, DisplayName = "Parity actor" }.Save();
        await roles.Define($"role:parity-reader:{suffix}", "Reader", ["global:read"]);
        await roles.Add($"role:parity-reader:{suffix}", target);
        await roles.Define($"role:parity-operator:{suffix}", "Operator", ["global:operate"]);
        await roles.Add($"role:parity-operator:{suffix}", actor);
        try
        {
            // The implicit roles exist with permissions: their permissions reach the bag, never a role claim.
            await roles.Define(RoleTokens.Everyone, "Everyone", ["global:everyone_parity"]);
            await roles.Define(RoleTokens.Authenticated, "Authenticated", ["global:authenticated_parity"]);
            var targetRequest = await RequestRoles(target);
            var actorRequest = await RequestRoles(actor);
            targetRequest.Should().Equal($"role:parity-reader:{suffix}");
            (await roles.Bag(target, true)).Permissions.Should().Contain(["global:everyone_parity", "global:authenticated_parity"]);

            using var scope = _fx.Services.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<EffectiveAccessResolver>();
            (await resolver.ResolveAsync(target)).Capabilities.Should().Contain(["global:everyone_parity", "global:authenticated_parity"],
                "the implicit roles' permissions stay in the effective view");
            var authorize = new CapturingAuthorize();
            await new AccessExplainer(resolver, authorize, roles).CanAsync(target, "read", "Orders");
            RoleClaims(authorize.Principal!).Should().Equal(targetRequest, "the forward explainer simulates what a request would carry");

            var impersonation = _fx.Services.GetRequiredService<ImpersonationService>();
            var grant = await impersonation.RequestAsync(actor, target, "parity", null);
            await impersonation.ApproveAsync(grant.Id, $"parity-approver:{suffix}", TimeSpan.FromMinutes(10));
            var signIns = new CapturingAuthentication();
            var controller = WithServices(new ImpersonationController(impersonation), signIns,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor)], "test")));
            (await controller.Start(grant.Id, default)).Should().BeOfType<OkObjectResult>();
            RoleClaims(signIns.Principal!).Should().BeEmpty("no session's cookie carries a role");
            RoleClaims(await Validated(signIns.Principal!)).Should().Equal(targetRequest, "an impersonation session's requests carry the target's roles");

            controller = WithServices(new ImpersonationController(impersonation), signIns, signIns.Principal!);
            (await controller.Stop()).Should().BeOfType<OkObjectResult>();
            RoleClaims(await Validated(signIns.Principal!)).Should().Equal(actorRequest, "stopping restores the actor's requests' roles");
        }
        finally
        {
            await roles.Delete(RoleTokens.Everyone);
            await roles.Delete(RoleTokens.Authenticated);
        }
    }

    [Fact]
    public async Task Role_and_overwrite_writes_are_refused_while_impersonating_and_succeed_otherwise()
    {
        var roles = _fx.Services.GetRequiredService<RoleCollection>();
        var options = _fx.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoleOptions>>();
        var suffix = Guid.NewGuid().ToString("N");
        var key = $"role:guarded:{suffix}";
        var resource = $"resource:guarded:{suffix}";
        var impersonating = WithRole(ImpersonationClaims.BuildImpersonatedIdentity($"guard-target:{suffix}", $"guard-op:{suffix}"), IdentityRoles.Operator);
        var operatorPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, $"guard-op:{suffix}"), new Claim(ClaimTypes.Role, IdentityRoles.Operator)], "test"));
        RoleManagementController RoleRoutes(ClaimsPrincipal user) => WithUser(new RoleManagementController(roles, options), user);
        PermissionOverwriteController OverwriteRoutes(ClaimsPrincipal user) => WithUser(new PermissionOverwriteController(roles), user);
        using var scope = _fx.Services.CreateScope();
        IdentityAccessController AccessRoutes(ClaimsPrincipal user) => WithUser(new IdentityAccessController(
            scope.ServiceProvider.GetRequiredService<EffectiveAccessResolver>(),
            scope.ServiceProvider.GetRequiredService<AccessExplainer>(), roles), user);
        var save = new RoleManagementController.SaveRoleRequest("Guarded", ["global:post"]);
        var deny = new PermissionOverwriteController.SaveOverwriteRequest(null, ["global:post"]);

        Refused((await RoleRoutes(impersonating).Put(key, save, default)).Result, "roles");
        (await roles.Get(key)).Should().BeNull();
        (await RoleRoutes(operatorPrincipal).Put(key, save, default)).Result.Should().BeOfType<OkObjectResult>();
        Refused((await RoleRoutes(impersonating).Patch(key, save with { Name = "Renamed" }, default)).Result, "roles");
        (await RoleRoutes(operatorPrincipal).Patch(key, save with { Name = "Renamed" }, default)).Result.Should().BeOfType<OkObjectResult>();
        Refused((await RoleRoutes(impersonating).AddMember(key, $"guard-member:{suffix}", default)).Result, "roles");
        (await RoleRoutes(operatorPrincipal).AddMember(key, $"guard-member:{suffix}", default)).Result.Should().BeOfType<OkObjectResult>();
        Refused(await RoleRoutes(impersonating).RemoveMember(key, $"guard-member:{suffix}", default), "roles");
        (await RoleRoutes(operatorPrincipal).RemoveMember(key, $"guard-member:{suffix}", default)).Should().BeOfType<NoContentResult>();

        Refused((await OverwriteRoutes(impersonating).Put(OverwriteSubjectKind.Role, key, resource, deny, default)).Result, "overwrites");
        (await roles.Overwrites(resource)).Items.Should().BeEmpty();
        (await OverwriteRoutes(operatorPrincipal).Put(OverwriteSubjectKind.Role, key, resource, deny, default)).Result.Should().BeOfType<OkObjectResult>();
        Refused(await OverwriteRoutes(impersonating).Delete(OverwriteSubjectKind.Role, key, resource, default), "overwrites");
        (await OverwriteRoutes(operatorPrincipal).Delete(OverwriteSubjectKind.Role, key, resource, default)).Should().BeOfType<NoContentResult>();

        await roles.Add(key, $"guard-member:{suffix}");
        Refused(await AccessRoutes(impersonating).Revoke($"guard-member:{suffix}", key, default), "roles");
        (await AccessRoutes(operatorPrincipal).Revoke($"guard-member:{suffix}", key, default)).Should().BeOfType<NoContentResult>();

        Refused(await RoleRoutes(impersonating).Delete(key, default), "roles");
        (await roles.Get(key)).Should().NotBeNull();
        (await RoleRoutes(operatorPrincipal).Delete(key, default)).Should().BeOfType<NoContentResult>();
    }

    private static void Refused(object? result, string what)
    {
        var refused = result.Should().BeOfType<ObjectResult>().Subject;
        refused.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        refused.Value!.ToString().Should().Contain($"changing {what} is blocked while impersonating");
    }

    private static T WithUser<T>(T controller, ClaimsPrincipal user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return controller;
    }

    private static T WithServices<T>(T controller, IAuthenticationService authentication, ClaimsPrincipal user) where T : ControllerBase
    {
        var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user, RequestServices = services } };
        return controller;
    }

    private static ClaimsPrincipal WithRole(ClaimsIdentity identity, string role)
    {
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(identity);
    }

    /// <summary>The roles a signed-in person's requests carry: nothing from the sign-in, the bag's on each validation.</summary>
    private async Task<IReadOnlyList<string>> RequestRoles(string subject)
    {
        using var scope = _fx.Services.CreateScope();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchSignIn(new Koan.Web.Auth.Contributors.AuthSignInContext
        {
            Provider = null, // the application's own sign-in, naming its person
            Identity = identity,
            Services = scope.ServiceProvider,
            HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
        }, default);
        identity.FindAll(ClaimTypes.Role).Should().BeEmpty("the cookie carries no role");
        return RoleClaims(await Validated(new ClaimsPrincipal(identity)));
    }

    /// <summary>The principal a request authorizes on, after the cookie pipeline validates <paramref name="cookie"/>.</summary>
    private async Task<ClaimsPrincipal> Validated(ClaimsPrincipal cookie)
    {
        using var scope = _fx.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var scheme = new AuthenticationScheme("test", null, typeof(CookieAuthenticationHandler));
        var inner = new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(), new AuthenticationTicket(cookie, "test"));
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchValidatePrincipal(
            new AuthValidatePrincipalContext { Inner = inner, Services = scope.ServiceProvider }, default);
        return inner.Principal.Should().NotBeNull("the principal stays valid").And.Subject.As<ClaimsPrincipal>();
    }

    private static IReadOnlyList<string> RoleClaims(ClaimsPrincipal principal)
        => principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Order(StringComparer.Ordinal).ToList();

    private sealed class CapturingAuthorize : IAuthorize
    {
        public ClaimsPrincipal? Principal { get; private set; }
        public Task<AuthorizeDecision> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default)
        {
            Principal = request.Subject;
            return Task.FromResult<AuthorizeDecision>(AuthorizeDecision.Allowed());
        }
    }

    private sealed class CapturingAuthentication : IAuthenticationService
    {
        public ClaimsPrincipal? Principal { get; private set; }
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            Principal = principal;
            return Task.CompletedTask;
        }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
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
