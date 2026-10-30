using System.Collections.Concurrent;
using System.Net;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Management;
using Koan.Web.Auth.Standing;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Revocation.Tests;

/// <summary>An application's word on a person, as a consumer's ban or time-out reports it: discovered, with no registration.</summary>
public sealed class PlaceStanding : ISubjectStandingContributor
{
    public static readonly ConcurrentDictionary<string, SubjectStanding> Reports = new(StringComparer.Ordinal);

    public Task<SubjectStanding?> Read(string subject, CancellationToken ct)
        => Task.FromResult(Reports.TryGetValue(subject, out var standing) ? standing : null);
}

/// <summary>
/// The seam the token endpoint consults at every mint. An application contributes to it, a report that a person may not
/// act refuses their agent without spending its token, and every lifecycle verb that closes a person closes their agents.
/// </summary>
public sealed class StandingSeamSpec(RevocationFixture fixture) : IClassFixture<RevocationFixture>, IAsyncLifetime
{
    private IDisposable? _host;
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

    private async Task<Agent> Connected(HttpClient browser, string person)
    {
        await RevocationFixture.SignIn(browser, person);
        return await fixture.Connect(browser, Client());
    }

    private async Task<HttpResponseMessage> Refresh(string clientId, string token)
    {
        using var http = fixture.Browser();
        return await RevocationFixture.Refresh(http, clientId, token);
    }

    [Fact]
    public async Task An_applications_time_out_refuses_the_refresh_and_the_agent_resumes_with_the_same_token_when_it_ends()
    {
        var person = Person();
        using var browser = fixture.Browser();
        var agent = await Connected(browser, person);

        PlaceStanding.Reports[person] = new SubjectStanding(false);
        using (var refused = await Refresh(agent.ClientId, agent.Tokens.Refresh))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync(RevocationFixture.Ct)).Should().Contain("invalid_grant").And.NotContain("rotated");
        }
        (await AgentGrant.Approvals(person, 10, RevocationFixture.Ct)).Should().ContainSingle("a report refuses; it does not end the approval");

        PlaceStanding.Reports.TryRemove(person, out _);
        using var resumed = await Refresh(agent.ClientId, agent.Tokens.Refresh);
        resumed.StatusCode.Should().Be(HttpStatusCode.OK, "a refused token was not spent, so this is no reuse");
    }

    [Fact]
    public async Task An_applications_roles_are_held_alongside_the_persons()
    {
        var person = Person();
        using var browser = fixture.Browser();
        var agent = await Connected(browser, person);

        PlaceStanding.Reports[person] = new SubjectStanding(true, Roles: ["place:moderator"]);
        using var refreshed = await Refresh(agent.ClientId, agent.Tokens.Refresh);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        PlaceStanding.Reports.TryRemove(person, out _);

        (await Tokens.Read(refreshed)).Roles.Should().Contain("place:moderator");
    }

    [Fact]
    public async Task A_status_written_directly_refuses_the_refresh_and_reactivating_resumes_it()
    {
        var person = Person();
        using var browser = fixture.Browser();
        var agent = await Connected(browser, person);
        var row = await Identity.Get(person, RevocationFixture.Ct);

        row!.Status = IdentityStatus.Suspended;
        await row.Save(RevocationFixture.Ct);
        using (var refused = await Refresh(agent.ClientId, agent.Tokens.Refresh))
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        row.Status = IdentityStatus.Active;
        await row.Save(RevocationFixture.Ct);
        using var resumed = await Refresh(agent.ClientId, agent.Tokens.Refresh);
        resumed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_erased_persons_agent_is_refused_and_their_approvals_are_gone()
    {
        var person = Person();
        using var browser = fixture.Browser();
        var agent = await Connected(browser, person);

        using (var scope = fixture.Services.CreateScope())
        {
            var receipt = await scope.ServiceProvider.GetRequiredService<IdentityLifecycleService>().EraseAsync(person, RevocationFixture.Ct);
            receipt.Complete.Should().BeTrue();
            receipt.Owners.Single(owner => owner.Owner == "Koan.Identity.Core").Counts["client-grants"].Should().Be(1);
        }

        (await AgentGrant.Approvals(person, 10, RevocationFixture.Ct)).Should().BeEmpty();
        using var refused = await Refresh(agent.ClientId, agent.Tokens.Refresh);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_code_approved_before_the_person_was_suspended_mints_nothing()
    {
        var person = Person();
        using var browser = fixture.Browser();
        await RevocationFixture.SignIn(browser, person);
        var code = await fixture.Approve(browser, Client());

        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IdentityLifecycleService>().SuspendAsync([person], RevocationFixture.Ct);

        using var http = fixture.Browser();
        using var refused = await RevocationFixture.Redeem(http, code);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(RevocationFixture.Ct)).Should().Contain("invalid_grant");
    }
}
