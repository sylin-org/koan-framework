using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Hooks;
using Xunit;
using AuthorizeRequest = Koan.Web.Extensions.Authorization.AuthorizeRequest;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0002 steps 1–2: the RBAC floor provider and the seam's provider-ladder semantics (first definitive
/// decision wins; default behavior on full defer).
/// </summary>
public sealed class AuthorizationSeamTests
{
    private static readonly RbacAuthorizationProvider Rbac = new();

    private static ClaimsPrincipal User(bool authenticated, params string[] roles)
    {
        var claims = new List<Claim>();
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        var identity = authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity(claims);
        return new ClaimsPrincipal(identity);
    }

    private static AuthorizeRequest Req(ClaimsPrincipal subject, string action, IReadOnlyCollection<string>? roles = null)
        => new() { Subject = subject, Action = action, RequiredRoles = roles };

    // ── RBAC floor provider ──────────────────────────────────────────────

    [Fact]
    public async Task Rbac_defers_when_no_role_requirement()
        => (await Rbac.EvaluateAsync(Req(User(true), "read"))).Should().BeNull();

    [Fact]
    public async Task Rbac_allows_when_subject_holds_a_required_role()
        => (await Rbac.EvaluateAsync(Req(User(true, "admin"), "delete", new[] { "admin" })))
            .Should().BeOfType<AuthorizeDecision.Allow>();

    [Fact]
    public async Task Rbac_forbids_authenticated_subject_lacking_the_role()
        => (await Rbac.EvaluateAsync(Req(User(true, "reader"), "delete", new[] { "admin" })))
            .Should().BeOfType<AuthorizeDecision.Forbid>();

    [Fact]
    public async Task Rbac_challenges_unauthenticated_subject_facing_a_requirement()
        => (await Rbac.EvaluateAsync(Req(User(false), "delete", new[] { "admin" })))
            .Should().BeOfType<AuthorizeDecision.Challenge>();

    // ── Authorizer ladder ────────────────────────────────────────────────

    private sealed class FixedProvider : IAuthorizationProvider
    {
        private readonly AuthorizeDecision? _decision;
        public FixedProvider(int order, AuthorizeDecision? decision) { Order = order; _decision = decision; }
        public int Order { get; }
        public Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
            => Task.FromResult(_decision);
    }

    private static Authorizer Ladder(AuthorizeDefault fallback, params IAuthorizationProvider[] providers)
        => new(providers, Microsoft.Extensions.Options.Options.Create(new AuthorizeOptions { DefaultDecision = fallback }));

    [Fact]
    public async Task Ladder_returns_the_first_non_deferring_provider_in_order()
    {
        var sut = Ladder(AuthorizeDefault.Forbid,
            new FixedProvider(100, AuthorizeDecision.Forbidden("late")),  // would forbid…
            new FixedProvider(0, AuthorizeDecision.Allowed()));           // …but order 0 runs first
        (await sut.AuthorizeAsync(Req(User(true), "x"))).Should().BeOfType<AuthorizeDecision.Allow>();
    }

    [Fact]
    public async Task Ladder_applies_default_Allow_when_all_providers_defer()
    {
        var sut = Ladder(AuthorizeDefault.Allow, new FixedProvider(0, null), new FixedProvider(1, null));
        (await sut.AuthorizeAsync(Req(User(true), "x"))).Should().BeOfType<AuthorizeDecision.Allow>();
    }

    [Fact]
    public async Task Ladder_applies_default_Forbid_when_configured_and_all_defer()
    {
        var sut = Ladder(AuthorizeDefault.Forbid, new FixedProvider(0, null));
        (await sut.AuthorizeAsync(Req(User(true), "x"))).Should().BeOfType<AuthorizeDecision.Forbid>();
    }
}
