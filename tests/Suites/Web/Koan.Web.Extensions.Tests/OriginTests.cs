using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0004 — the <c>origin</c> gate dimension: WHERE a call arrived (transport trust), distinct from WHO the
/// caller is. A framework-stamped, un-forgeable <c>koan:origin</c> claim drives the <c>origin:local|internal|remote</c>
/// gate term. The load-bearing property pinned here: origin is ORTHOGONAL to authentication — a STDIO call is
/// <c>local</c> yet anonymous, so it must satisfy <c>origin:local</c> without being signed in.
/// </summary>
public sealed class OriginTests
{
    [Access(read: "origin:local")]
    private sealed class LocalOnlyEntity { }

    [Access(read: "origin:internal")]
    private sealed class InternalOnlyEntity { }

    // The ADR's headline composite: removable by a LOCAL caller OR an admin (OR across an origin bag + an identity bag).
    [Access(remove: "origin:local, is:admin")]
    private sealed class LocalOrAdminEntity { }

    private static readonly EntityFloorAuthorizationProvider Provider = new(new AccessGateCache());

    private static Task<AuthorizeDecision?> Eval(Type entity, string action, ClaimsPrincipal user)
        => Provider.EvaluateAsync(new AuthorizeRequest { Subject = user, Action = action, Resource = entity });

    private static ClaimsPrincipal Anon(OriginTier? origin = null)
    {
        var p = new ClaimsPrincipal(new ClaimsIdentity());
        return origin is { } t ? OriginStamp.Apply(p, t) : p;
    }

    private static ClaimsPrincipal Admin(OriginTier origin)
        => OriginStamp.Apply(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "admin") }, "test")), origin);

    // ── the orthogonal-to-identity property (the headline) ───────────────────────────────────────────────────────
    [Fact]
    public async Task An_anonymous_local_caller_satisfies_origin_local()
        => (await Eval(typeof(LocalOnlyEntity), EntityAuthorizeActions.Read, Anon(OriginTier.Local)))
            .Should().BeOfType<AuthorizeDecision.Allow>("origin is a transport marker — a STDIO caller is local yet anonymous");

    [Fact]
    public async Task A_remote_caller_is_denied_origin_local()
        => (await Eval(typeof(LocalOnlyEntity), EntityAuthorizeActions.Read, Anon(OriginTier.Remote)))
            .Should().NotBeOfType<AuthorizeDecision.Allow>("a remote-stamped caller is not local");

    [Fact]
    public async Task An_unstamped_caller_is_denied_origin_local()
        => (await Eval(typeof(LocalOnlyEntity), EntityAuthorizeActions.Read, Anon()))
            .Should().NotBeOfType<AuthorizeDecision.Allow>("no origin claim → fail-closed");

    [Fact]
    public async Task Internal_matches_only_an_internal_stamp()
    {
        (await Eval(typeof(InternalOnlyEntity), EntityAuthorizeActions.Read, Anon(OriginTier.Internal)))
            .Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(InternalOnlyEntity), EntityAuthorizeActions.Read, Anon(OriginTier.Remote)))
            .Should().NotBeOfType<AuthorizeDecision.Allow>("remote is not internal");
        (await Eval(typeof(InternalOnlyEntity), EntityAuthorizeActions.Read, Anon(OriginTier.Local)))
            .Should().NotBeOfType<AuthorizeDecision.Allow>("local is a distinct tier, not a superset of internal");
    }

    [Fact]
    public async Task Origin_ORs_with_an_identity_bag()
    {
        // origin:local, is:admin — either suffices.
        (await Eval(typeof(LocalOrAdminEntity), EntityAuthorizeActions.Remove, Anon(OriginTier.Local)))
            .Should().BeOfType<AuthorizeDecision.Allow>("a local caller may remove (origin bag)");
        (await Eval(typeof(LocalOrAdminEntity), EntityAuthorizeActions.Remove, Admin(OriginTier.Remote)))
            .Should().BeOfType<AuthorizeDecision.Allow>("a remote admin may remove (identity bag)");
        (await Eval(typeof(LocalOrAdminEntity), EntityAuthorizeActions.Remove, Anon(OriginTier.Remote)))
            .Should().NotBeOfType<AuthorizeDecision.Allow>("a remote non-admin may not");
    }

    // ── parser grammar ───────────────────────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("origin:local", Origin.Local)]
    [InlineData("origin:internal", Origin.Internal)]
    [InlineData("origin:remote", Origin.Remote)]
    public void Origin_term_lowers_to_an_unauthenticated_claim_bag(string term, string expected)
    {
        var gate = AccessGateParser.ParseValue(term, "X", "read");
        var bag = gate.AnyOf.Should().ContainSingle().Subject;
        bag.Authenticated.Should().BeFalse("origin does not imply authentication");
        var grant = bag.HasAllOf.Should().ContainSingle().Subject.Should().BeOfType<Grant.Claim>().Subject;
        grant.Type.Should().Be(Origin.ClaimType);
        grant.Value.Should().Be(expected);
    }

    [Fact]
    public void An_unknown_origin_value_fails_fast()
    {
        var act = () => AccessGateParser.ParseValue("origin:lan", "X", "read");
        act.Should().Throw<AccessGateException>().WithMessage("*unknown origin 'lan'*");
    }

    [Fact]
    public void Origin_helper_emits_the_canonical_string()
        => Access.Origin(OriginTier.Local).Should().Be("origin:local");

    // ── OriginStamp: server-trusted, un-forgeable, identity-preserving ───────────────────────────────────────────
    [Fact]
    public void Apply_strips_a_client_forged_origin_and_writes_the_framework_value()
    {
        var forged = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(Origin.ClaimType, "local") }, "test"));
        var stamped = OriginStamp.Apply(forged, OriginTier.Remote);

        stamped.FindAll(Origin.ClaimType).Select(c => c.Value)
            .Should().ContainSingle().Which.Should().Be("remote", "the client's forged 'local' is stripped, the real tier wins");
    }

    [Fact]
    public void Apply_does_not_authenticate_an_anonymous_caller()
    {
        var stamped = OriginStamp.Apply(new ClaimsPrincipal(new ClaimsIdentity()), OriginTier.Local);
        (stamped.Identity?.IsAuthenticated ?? false).Should().BeFalse("the origin carrier identity is unauthenticated");
        OriginStamp.IsStamped(stamped).Should().BeTrue();
    }

    [Fact]
    public void Apply_preserves_an_authenticated_identity()
    {
        var authed = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "ada") }, "test"));
        var stamped = OriginStamp.Apply(authed, OriginTier.Internal);
        stamped.Identity!.IsAuthenticated.Should().BeTrue("stamping must not drop the caller's authentication");
        stamped.FindFirst(Origin.ClaimType)!.Value.Should().Be("internal");
    }

    // ── OriginResolver / OriginOptions: never local, fail-closed internal ────────────────────────────────────────
    [Fact]
    public void A_networked_caller_is_never_local()
    {
        var opts = new OriginOptions();
        opts.InternalNetworks.Add("10.0.0.0/8");
        OriginResolver.FromIp(IPAddress.Parse("10.1.2.3"), opts).Should().Be(OriginTier.Internal, "declared LAN → internal");
        OriginResolver.FromIp(IPAddress.Parse("203.0.113.7"), opts).Should().Be(OriginTier.Remote, "outside → remote");
        OriginResolver.FromIp(IPAddress.Loopback, opts).Should().Be(OriginTier.Remote, "loopback HTTP is remote unless declared internal");
    }

    [Fact]
    public void Internal_is_fail_closed_without_declared_networks()
    {
        OriginResolver.FromIp(IPAddress.Parse("10.1.2.3"), OriginOptions.Empty).Should().Be(OriginTier.Remote,
            "no declared internal networks → nothing is internal (un-spoofable)");
        OriginResolver.FromIp(null, OriginOptions.Empty).Should().Be(OriginTier.Remote, "no IP → remote");
    }
}
