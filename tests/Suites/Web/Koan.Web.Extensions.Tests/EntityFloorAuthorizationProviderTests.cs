using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// ARCH-0092 (§D) — the built-in entity-floor rung. Evaluates an entity's declarative access floor
/// (<c>[AllowAnonymous]</c> / <c>[Authorize]</c> / <c>[RequireScope]</c>) reflected off the resource type,
/// so the same declaration is honored on every surface through the one seam.
/// </summary>
public sealed class EntityFloorAuthorizationProviderTests
{
    private sealed class PlainGadget { }

    [AllowAnonymous]
    private sealed class OpenGadget { }

    [RequireScope("gadgets:write")]
    private sealed class ScopedGadget { }

    [Authorize]
    private sealed class AuthGadget { }

    [Authorize(Roles = "admin")]
    private sealed class AdminGadget { }

    private static readonly EntityFloorAuthorizationProvider Provider = new();

    private static async Task<AuthorizeDecision?> Evaluate(System.Type entity, ClaimsPrincipal user)
        => await Provider.EvaluateAsync(new AuthorizeRequest
        {
            Subject = user,
            Action = EntityAuthorizeActions.Write,
            Resource = entity,
        });

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated(string[]? scopes = null, string[]? roles = null)
    {
        var claims = new List<Claim>();
        if (scopes is { Length: > 0 }) claims.Add(new Claim("scope", string.Join(' ', scopes)));
        foreach (var r in roles ?? System.Array.Empty<string>()) claims.Add(new Claim(ClaimTypes.Role, r));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    [Fact]
    public async Task No_floor_attribute_defers()
        => (await Evaluate(typeof(PlainGadget), Anonymous())).Should().BeNull("no floor → allow-by-default");

    [Fact]
    public async Task Resource_that_is_not_a_type_defers()
    {
        var d = await Provider.EvaluateAsync(new AuthorizeRequest
        {
            Subject = Anonymous(),
            Action = EntityAuthorizeActions.Read,
            Resource = "not-a-type",
        });
        d.Should().BeNull();
    }

    [Fact]
    public async Task AllowAnonymous_allows_even_unauthenticated()
        => (await Evaluate(typeof(OpenGadget), Anonymous())).Should().BeOfType<AuthorizeDecision.Allow>();

    [Fact]
    public async Task RequireScope_unauthenticated_challenges()
        => (await Evaluate(typeof(ScopedGadget), Anonymous())).Should().BeOfType<AuthorizeDecision.Challenge>();

    [Fact]
    public async Task RequireScope_with_matching_scope_allows()
        => (await Evaluate(typeof(ScopedGadget), Authenticated(scopes: new[] { "gadgets:write" })))
            .Should().BeOfType<AuthorizeDecision.Allow>();

    [Fact]
    public async Task RequireScope_missing_scope_forbids()
        => (await Evaluate(typeof(ScopedGadget), Authenticated(scopes: new[] { "gadgets:read" })))
            .Should().BeOfType<AuthorizeDecision.Forbid>();

    [Fact]
    public async Task Authorize_unauthenticated_challenges()
        => (await Evaluate(typeof(AuthGadget), Anonymous())).Should().BeOfType<AuthorizeDecision.Challenge>();

    [Fact]
    public async Task Authorize_authenticated_allows()
        => (await Evaluate(typeof(AuthGadget), Authenticated())).Should().BeOfType<AuthorizeDecision.Allow>();

    [Fact]
    public async Task Authorize_roles_without_role_forbids()
        => (await Evaluate(typeof(AdminGadget), Authenticated(roles: new[] { "user" })))
            .Should().BeOfType<AuthorizeDecision.Forbid>();

    [Fact]
    public async Task Authorize_roles_with_role_allows()
        => (await Evaluate(typeof(AdminGadget), Authenticated(roles: new[] { "admin" })))
            .Should().BeOfType<AuthorizeDecision.Allow>();
}
