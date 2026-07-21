using System.Linq;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Koan.Security.Trust.Dev;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0003 §2.3: the <c>?_as=</c> dev persona override. <b>Default is anonymous</b> (no <c>?_as</c> ⇒ no
/// principal); <c>?_as=&lt;sub&gt;&amp;_roles=</c> sets a transient persona; <c>?_as=anonymous</c> stays
/// anonymous; a real principal is never overwritten; <c>Enabled=false</c> is a no-op. (Development-only
/// insertion via the WEB-0069 contributor.)
/// </summary>
public sealed class DevIdentityTests
{
    private static (HttpContext Context, ClaimsPrincipal? Resolved) Run(
        DevIdentityOptions options,
        string queryString = "",
        ClaimsPrincipal? existing = null)
    {
        var ctx = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(queryString)) ctx.Request.QueryString = new QueryString(queryString);
        if (existing is not null) ctx.User = existing;
        return (ctx, DevIdentity.Resolve(ctx, options));
    }

    [Fact]
    public void Default_request_with_no_persona_stays_anonymous()
    {
        // SEC-0003 §2.1/§2.3 — no ?_as ⇒ no principal (public by default).
        var result = Run(new DevIdentityOptions());
        result.Resolved.Should().BeNull();
        result.Context.User.Identity?.IsAuthenticated.Should().NotBe(true);
    }

    [Fact]
    public void Persona_override_sets_subject_and_roles()
    {
        var result = Run(new DevIdentityOptions(), "?_as=alice&_roles=reader,editor");
        result.Resolved!.FindFirst("sub")!.Value.Should().Be("alice");
        result.Resolved.FindAll(ClaimTypes.Role).Select(c => c.Value).Should().BeEquivalentTo("reader", "editor");
    }

    [Fact]
    public void As_anonymous_stays_unauthenticated()
    {
        Run(new DevIdentityOptions(), "?_as=anonymous").Resolved.Should().BeNull();
    }

    [Fact]
    public void Already_authenticated_principal_is_not_overwritten()
    {
        var existing = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "real") }, "cookie"));
        var result = Run(new DevIdentityOptions(), existing: existing);
        result.Resolved.Should().BeNull();
        result.Context.User.FindFirst("sub")!.Value.Should().Be("real");
    }

    [Fact]
    public void Disabled_does_not_set_an_identity()
    {
        Run(new DevIdentityOptions { Enabled = false }).Resolved.Should().BeNull();
    }
}
