using System.Net;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Identity.Roles;
using Koan.Web.Auth.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Revocation.Tests;

/// <summary>A cookie short enough that a request renews it within a spec.</summary>
public sealed class ShortCookieFixture : RevocationFixture
{
    protected override void Compose(IServiceCollection services)
        => services.PostConfigure<CookieAuthenticationOptions>(
            AuthenticationExtensions.CookieScheme,
            options => options.ExpireTimeSpan = TimeSpan.FromSeconds(4));
}

/// <summary>
/// A sliding cookie is reissued from a request: the roles projected onto that request must not ride into it, or the
/// reissue would freeze them there and a later removal would stop nothing.
/// </summary>
public sealed class SlidingCookieSpec(ShortCookieFixture fixture) : IClassFixture<ShortCookieFixture>
{
    [Fact]
    public async Task A_renewed_cookie_carries_no_projected_role()
    {
        using var app = AppHost.PushScope(fixture.Services);
        var roles = fixture.Services.GetRequiredService<RoleCollection>();
        if (await roles.Get(RevocationProbeController.GuardedRole) is null)
            await roles.Define(RevocationProbeController.GuardedRole, "Guarded", ["global:probe"]);
        var person = $"person:{Guid.CreateVersion7():N}";
        await roles.Add(RevocationProbeController.GuardedRole, person);
        using var browser = fixture.Browser();
        await RevocationFixture.SignIn(browser, person);

        // Past half of the cookie's life, the next request renews it.
        await Task.Delay(TimeSpan.FromSeconds(2.2), RevocationFixture.Ct);
        using (var renewing = await browser.GetAsync("/probe/guarded", RevocationFixture.Ct))
        {
            renewing.StatusCode.Should().Be(HttpStatusCode.OK);
            renewing.Headers.TryGetValues("Set-Cookie", out var issued).Should().BeTrue("the sliding cookie renews on this request");
            issued!.Should().Contain(cookie => cookie.StartsWith(".AspNetCore.Koan.cookie", StringComparison.Ordinal));
        }

        await roles.Remove(RevocationProbeController.GuardedRole, person);

        using var denied = await browser.GetAsync("/probe/guarded", RevocationFixture.Ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Redirect, "the renewed cookie froze nothing");
    }
}
