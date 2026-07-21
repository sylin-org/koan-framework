using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Management;
using Koan.Identity.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P1 / Layer-1 API acceptance — the Reference = Intent management surfaces. Exercises the controllers'
/// own subject-scoping directly against the real services on the
/// shared offline host; ASP.NET's <c>[Authorize]</c> gate is framework-enforced and not re-proven here.
/// </summary>
[Collection("identity")]
public sealed class IdentityManagementApiSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public IdentityManagementApiSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    private IdentitySelfServiceController SelfService(string subject)
        => WithUser(new IdentitySelfServiceController(
            _fx.Services.GetRequiredService<SessionService>(),
            _fx.Services.GetRequiredService<IdentityLinkService>()), subject);

    private IdentityAdminController Admin()
        => WithUser(new IdentityAdminController(_fx.Services.GetRequiredService<IdentityLifecycleService>()), "operator", IdentityRoles.Operator);

    private static T WithUser<T>(T controller, string subject, params string[] roles) where T : ControllerBase
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        };
        return controller;
    }

    private static T? Value<T>(ActionResult<T> result) where T : class
        => (result.Result as OkObjectResult)?.Value as T;

    [Fact]
    public async Task Self_service_profile_is_scoped_to_the_caller()
    {
        await new Identity { Id = "api-me", DisplayName = "API Me" }.Save();

        var me = Value(await SelfService("api-me").Profile(default));
        me.Should().NotBeNull();
        me!.Id.Should().Be("api-me");

        // A different caller never sees this person through their own profile route.
        var other = await SelfService("someone-else").Profile(default);
        (other.Result is NotFoundResult || (other.Result as OkObjectResult)?.Value is null).Should().BeTrue();
    }

    [Fact]
    public async Task Self_service_sign_out_others_revokes_via_the_controller()
    {
        const string id = "api-sessions";
        await new Identity { Id = id, DisplayName = "C" }.Save();
        var ctrl = SelfService(id);
        var sessions = _fx.Services.GetRequiredService<SessionService>();
        var keep = await sessions.RecordAsync(id, "Phone", "Safari", "iOS", null);
        await sessions.RecordAsync(id, "Laptop", "Firefox", "Linux", null);

        var result = ctrl.SignOutOthers(new IdentitySelfServiceController.SignOutOthersRequest(keep.Id), default);
        var ok = (await result).Result as OkObjectResult;
        ok.Should().NotBeNull();

        (await sessions.ListAsync(id)).Count(s => s.IsActive).Should().Be(1, "only the current session survives");
    }

    [Fact]
    public async Task Operator_api_lists_suspends_and_deletes()
    {
        await new Identity { Id = "op-target", DisplayName = "Op Target" }.Save();

        var list = Value(await Admin().List(q: "Op Target", size: 10));
        list.Should().NotBeNull();
        list!.Should().Contain(i => i.Id == "op-target");

        var suspend = (await Admin().Suspend(new IdentityAdminController.BulkRequest(new() { "op-target" }), default)).Result as OkObjectResult;
        ((IdentityLifecycleService.BulkResult)suspend!.Value!).Succeeded.Should().Contain("op-target");
        (await Identity.Get("op-target"))!.Status.Should().Be(IdentityStatus.Suspended);

        var del = (await Admin().Delete("op-target", default)).Result as OkObjectResult;
        del.Should().NotBeNull();
        (await Identity.Get("op-target")).Should().BeNull();
    }
}
