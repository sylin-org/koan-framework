using System.Linq;
using System.Security.Claims;
using AwesomeAssertions;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Hooks;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0001 Phase 2 (2f): the Tier-0 RBAC floor. Unrestricted by default; role-gated when required;
/// challenge (not forbid) for an unauthenticated caller facing a requirement.
/// </summary>
public sealed class RbacAuthorizerTests
{
    private static readonly RbacAuthorizer Sut = new();

    private static ClaimsPrincipal User(bool authenticated, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r));
        var identity = authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity(claims);
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void No_required_roles_is_allowed()
        => Sut.Authorize(User(true), "read").Should().BeOfType<AuthorizeDecision.Allow>();

    [Fact]
    public void Holding_a_required_role_is_allowed()
        => Sut.Authorize(User(true, "admin"), "delete", new[] { "admin" }).Should().BeOfType<AuthorizeDecision.Allow>();

    [Fact]
    public void Authenticated_without_a_required_role_is_forbidden()
        => Sut.Authorize(User(true, "reader"), "delete", new[] { "admin" }).Should().BeOfType<AuthorizeDecision.Forbid>();

    [Fact]
    public void Unauthenticated_facing_a_requirement_is_challenged()
        => Sut.Authorize(User(false), "delete", new[] { "admin" }).Should().BeOfType<AuthorizeDecision.Challenge>();
}
