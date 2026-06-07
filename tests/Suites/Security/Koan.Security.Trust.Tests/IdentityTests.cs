using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Hosting.App;
using Koan.Security.Trust;
using Xunit;

namespace Koan.Security.Trust.Tests;

/// <summary>
/// SEC-0001 Phase 2 (2e): the ambient identity projects a principal uniformly, and Identity.Current
/// surfaces the current request's principal through the ambient AppHost provider.
/// </summary>
public sealed class IdentityTests
{
    private static ClaimsPrincipal Principal(string sub, params string[] roles)
    {
        var claims = new List<Claim> { new("sub", sub) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    [Fact]
    public void KoanIdentity_projects_subject_and_roles()
    {
        var id = new KoanIdentity(Principal("alice", "admin", "reader"));

        id.IsAuthenticated.Should().BeTrue();
        id.Id.Should().Be("alice");
        id.Roles.Should().BeEquivalentTo("admin", "reader");
        id.Is("admin").Should().BeTrue();
        id.Is("root").Should().BeFalse();
    }

    [Fact]
    public void Null_principal_is_unauthenticated()
    {
        var id = new KoanIdentity(null);
        id.IsAuthenticated.Should().BeFalse();
        id.Id.Should().BeNull();
        id.Roles.Should().BeEmpty();
        id.Is("admin").Should().BeFalse();
    }

    [Fact]
    public void Current_surfaces_the_request_principal_via_ambient_host()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = Principal("bob", "editor") };

        using (AppHost.PushScope(sp)) // flow-scoped, parallel-safe — no global pollution
        {
            Identity.Current.IsAuthenticated.Should().BeTrue();
            Identity.Current.Id.Should().Be("bob");
            Identity.Current.Is("editor").Should().BeTrue();
        }
    }
}
