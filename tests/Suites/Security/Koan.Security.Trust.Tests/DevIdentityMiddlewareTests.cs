using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Koan.Security.Trust.Dev;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0001 Phase 2 (Rung 0): the zero-config dev identity fills in an unauthenticated request as a dev
/// principal, supports the <c>?_as=</c>/<c>_roles=</c> persona switch and <c>?_as=anonymous</c> opt-out,
/// and never overwrites a real principal. (Development-only insertion is enforced by KoanWebAuthStartupFilter.)
/// </summary>
public sealed class DevIdentityMiddlewareTests
{
    private static async Task<HttpContext> Run(DevIdentityOptions options, string queryString = "", ClaimsPrincipal? existing = null)
    {
        var ctx = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(queryString)) ctx.Request.QueryString = new QueryString(queryString);
        if (existing is not null) ctx.User = existing;
        var middleware = new KoanDevIdentityMiddleware(_ => Task.CompletedTask, Options.Create(options));
        await middleware.InvokeAsync(ctx);
        return ctx;
    }

    [Fact]
    public async Task Unauthenticated_request_gets_the_default_dev_identity()
    {
        var ctx = await Run(new DevIdentityOptions());
        ctx.User.Identity!.IsAuthenticated.Should().BeTrue();
        ctx.User.FindFirst("sub")!.Value.Should().Be("dev");
        ctx.User.IsInRole("admin").Should().BeTrue();
    }

    [Fact]
    public async Task Persona_override_sets_subject_and_roles()
    {
        var ctx = await Run(new DevIdentityOptions(), "?_as=alice&_roles=reader,editor");
        ctx.User.FindFirst("sub")!.Value.Should().Be("alice");
        ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Should().BeEquivalentTo("reader", "editor");
    }

    [Fact]
    public async Task As_anonymous_stays_unauthenticated()
    {
        var ctx = await Run(new DevIdentityOptions(), "?_as=anonymous");
        ctx.User.Identity?.IsAuthenticated.Should().NotBe(true);
    }

    [Fact]
    public async Task Already_authenticated_principal_is_not_overwritten()
    {
        var existing = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "real") }, "cookie"));
        var ctx = await Run(new DevIdentityOptions(), existing: existing);
        ctx.User.FindFirst("sub")!.Value.Should().Be("real");
    }

    [Fact]
    public async Task Disabled_does_not_set_an_identity()
    {
        var ctx = await Run(new DevIdentityOptions { Enabled = false });
        ctx.User.Identity?.IsAuthenticated.Should().NotBe(true);
    }
}
