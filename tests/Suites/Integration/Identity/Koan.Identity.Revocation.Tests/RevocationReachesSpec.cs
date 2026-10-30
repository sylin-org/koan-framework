using System.Net;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Management;
using Koan.Identity.Roles;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Revocation.Tests;

/// <summary>
/// What is taken from a person reaches what they hold: the page their cookie opens on the next request, and what their
/// connected agents can mint on their next refresh.
/// </summary>
public sealed class RevocationReachesSpec(RevocationFixture fixture) : IClassFixture<RevocationFixture>, IAsyncLifetime
{
    private IDisposable? _host;
    private RoleCollection Roles => fixture.Services.GetRequiredService<RoleCollection>();
    private static string Person() => $"person:{Guid.CreateVersion7():N}";
    private static string Client() => $"client:{Guid.CreateVersion7():N}";

    public ValueTask InitializeAsync()
    {
        _host = AppHost.PushScope(fixture.Services);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureRole(string key)
    {
        if (await Roles.Get(key) is null) await Roles.Define(key, key, ["global:probe"]);
    }

    private async Task<IdentityLifecycleService.BulkResult> Suspend(string person)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityLifecycleService>().SuspendAsync([person], RevocationFixture.Ct);
    }

    /// <summary>A browser the role does not open is sent to the access-denied page.</summary>
    private static async Task ShouldBeDenied(HttpClient browser)
    {
        using var denied = await browser.GetAsync("/probe/guarded", RevocationFixture.Ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Redirect);
        denied.Headers.Location!.ToString().Should().Contain("AccessDenied");
    }

    private static async Task ShouldBeRefused(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(RevocationFixture.Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task A_role_removed_from_a_person_stops_authorizing_their_next_request_without_a_new_sign_in()
    {
        await EnsureRole(RevocationProbeController.GuardedRole);
        var person = Person();
        await Roles.Add(RevocationProbeController.GuardedRole, person);
        using var browser = fixture.Browser();
        await RevocationFixture.SignIn(browser, person);
        (await browser.GetAsync("/probe/guarded", RevocationFixture.Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await Roles.Remove(RevocationProbeController.GuardedRole, person);

        await ShouldBeDenied(browser);
        (await browser.GetAsync("/me", RevocationFixture.Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the person stays signed in");
    }

    [Fact]
    public async Task A_role_granted_to_a_signed_in_person_authorizes_their_next_request()
    {
        await EnsureRole(RevocationProbeController.GuardedRole);
        var person = Person();
        using var browser = fixture.Browser();
        await RevocationFixture.SignIn(browser, person);
        await ShouldBeDenied(browser);

        await Roles.Add(RevocationProbeController.GuardedRole, person);

        (await browser.GetAsync("/probe/guarded", RevocationFixture.Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_suspended_persons_agent_is_refused_its_refresh_and_loses_its_approval()
    {
        var person = Person();
        using var browser = fixture.Browser();
        await RevocationFixture.SignIn(browser, person);
        var agent = await fixture.Connect(browser, Client());
        (await AgentGrant.Approvals(person, 10, RevocationFixture.Ct)).Should().ContainSingle();

        (await Suspend(person)).Failed.Should().BeEmpty();

        using var http = fixture.Browser();
        using var refused = await RevocationFixture.Refresh(http, agent.ClientId, agent.Tokens.Refresh);
        await ShouldBeRefused(refused);
        (await AgentGrant.Approvals(person, 10, RevocationFixture.Ct)).Should().BeEmpty("reactivating the person brings no agent back");
    }

    [Fact]
    public async Task A_live_persons_refresh_carries_their_current_roles_and_name()
    {
        const string later = "role:granted-later";
        await EnsureRole(RevocationProbeController.GuardedRole);
        await EnsureRole(later);
        var person = Person();
        await Roles.Add(RevocationProbeController.GuardedRole, person);
        using var browser = fixture.Browser();
        await RevocationFixture.SignIn(browser, person);
        var agent = await fixture.Connect(browser, Client());
        agent.Tokens.Roles.Should().Contain(RevocationProbeController.GuardedRole);

        await Roles.Remove(RevocationProbeController.GuardedRole, person);
        await Roles.Add(later, person);
        var named = await Identity.Get(person, RevocationFixture.Ct);
        named!.DisplayName = "As renamed";
        await named.Save(RevocationFixture.Ct);

        using var http = fixture.Browser();
        using var refreshed = await RevocationFixture.Refresh(http, agent.ClientId, agent.Tokens.Refresh);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync(RevocationFixture.Ct));
        var tokens = await Tokens.Read(refreshed);

        tokens.Roles.Should().Contain(later).And.NotContain(RevocationProbeController.GuardedRole);
        tokens.Name.Should().Be("As renamed");
    }
}
