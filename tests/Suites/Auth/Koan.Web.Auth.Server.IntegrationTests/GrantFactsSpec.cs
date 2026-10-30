using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Authorization;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0005 (amended 2026-10-30) — each approval is its own grant: it says when it was issued and to which client, it
/// revokes alone and forgets the person's remembered consent, and a person's live approvals read newest first without
/// expired rows. A grant saved before Koan recorded these facts still refreshes and revokes.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class GrantFactsSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _fx;
    public GrantFactsSpec(OAuthFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private string Resource => $"{_fx.BaseUrl}/mcp";
    private const string Redirect = "http://127.0.0.1:7789/cb";

    [Fact]
    public async Task A_device_grant_records_when_it_was_issued_and_its_client()
    {
        await RegisterDeviceClient("facts-device", "Companion Lobby");
        var before = DateTimeOffset.UtcNow;

        var refresh = await DeviceFlow("facts-device", "facts-person");

        var after = DateTimeOffset.UtcNow;
        var grant = await GrantBehind(refresh);
        grant.Subject.Should().Be("facts-person");
        grant.IssuedAt.Should().NotBeNull("the authorization server records when it issued the grant");
        grant.IssuedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        grant.ExpiresAt.Should().Be(grant.IssuedAt + TimeSpan.FromDays(30), "issue and expiry are read from one clock");
        grant.ClientId.Should().Be("facts-device");
        grant.ClientName.Should().Be("Companion Lobby", "the name the client registered");
    }

    [Fact]
    public async Task A_client_that_registered_no_name_leaves_the_grant_without_one()
    {
        await RegisterDeviceClient("facts-nameless", "");

        var grant = await GrantBehind(await DeviceFlow("facts-nameless", "facts-nameless-person"));

        grant.ClientId.Should().Be("facts-nameless");
        grant.ClientName.Should().BeNull("a consumer falls back to the client id");
    }

    [Fact]
    public async Task Two_device_approvals_with_the_same_scopes_are_two_grants_each_with_its_time_and_client()
    {
        await RegisterDeviceClient("facts-delegate", "Delegate");

        var first = await DeviceFlow("facts-delegate", "facts-two-person");
        var second = await DeviceFlow("facts-delegate", "facts-two-person");

        var a = await GrantBehind(first);
        var b = await GrantBehind(second);
        b.Id.Should().NotBe(a.Id, "each approval is its own revocable grant");
        foreach (var grant in new[] { a, b })
        {
            grant.IssuedAt.Should().NotBeNull();
            grant.ClientId.Should().Be("facts-delegate");
            grant.ClientName.Should().Be("Delegate");
        }
        b.IssuedAt.Should().BeOnOrAfter(a.IssuedAt!.Value);
    }

    [Fact]
    public async Task Revoking_one_approval_leaves_the_other_able_to_refresh()
    {
        await RegisterDeviceClient("facts-revoke-one", "Delegate");
        var first = await DeviceFlow("facts-revoke-one", "facts-revoke-person");
        var second = await DeviceFlow("facts-revoke-one", "facts-revoke-person");

        (await (await GrantBehind(first)).Remove(Ct)).Should().BeTrue();

        using var http = _fx.NewClient();
        var revoked = await Refresh(http, "facts-revoke-one", first);
        revoked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await revoked.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");

        var kept = await Refresh(http, "facts-revoke-one", second);
        kept.StatusCode.Should().Be(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_persons_live_approvals_read_newest_first_bounded_and_without_expired_rows()
    {
        // The read is the store's: the user-code allowance bounds how many real approvals one spec class may run, so
        // these rows are saved as the authorization server saves an approval.
        const string person = "facts-list-person";
        var now = DateTimeOffset.UtcNow;
        Task<AgentGrant> Approval(string subject, TimeSpan age, TimeSpan life)
            => new AgentGrant
            {
                Subject = subject, Capability = "mcp:grant:facts-list:scopes", Resource = Resource,
                IssuedAt = now - age, ExpiresAt = now - age + life, ClientId = "facts-list", ClientName = "Lister",
            }.Save(Ct);

        var oldest = await Approval(person, TimeSpan.FromDays(3), TimeSpan.FromDays(30));
        var newest = await Approval(person, TimeSpan.FromMinutes(1), TimeSpan.FromDays(30));
        var middle = await Approval(person, TimeSpan.FromDays(1), TimeSpan.FromDays(30));
        await Approval(person, TimeSpan.FromDays(31), TimeSpan.FromDays(30)); // expired yesterday
        await Approval("facts-list-other", TimeSpan.Zero, TimeSpan.FromDays(30)); // another person's
        await new AgentGrant { Subject = person, Capability = "is:admin", Resource = "*", IssuedAt = now }.Save(Ct); // no approval

        (await AgentGrant.Approvals(person, 10, Ct)).Select(g => g.Id).Should()
            .Equal([newest.Id, middle.Id, oldest.Id], "live approvals only, newest first");
        (await AgentGrant.Approvals(person, 2, Ct)).Select(g => g.Id).Should()
            .Equal([newest.Id, middle.Id], "the read is bounded");
    }

    [Fact]
    public async Task Revoking_one_approval_brings_back_the_consent_page_while_the_other_still_refreshes()
    {
        const string person = "facts-forget-person";
        await RegisterCodeClient("facts-forget");
        using var browser = _fx.NewClient();
        (await browser.GetAsync($"/test/signin?sub={person}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (first, firstAsked) = await Connect(browser, "facts-forget");
        var (second, secondAsked) = await Connect(browser, "facts-forget");
        firstAsked.Should().BeTrue("the first connection asks the person");
        secondAsked.Should().BeFalse("the person's consent is remembered");

        (await (await GrantBehind(first)).Remove(Ct)).Should().BeTrue();

        var (_, challenge) = OAuthTestFlows.Pkce();
        var next = await browser.GetAsync(Authorize("facts-forget", challenge), Ct);
        next.StatusCode.Should().Be(HttpStatusCode.Redirect);
        next.Headers.Location!.ToString().Should().NotStartWith(Redirect, "revoking an approval forgets the person's consent");
        OAuthTestFlows.Query(next.Headers.Location!.ToString(), "rid").Should().NotBeEmpty("the consent page asks again");

        var kept = await Refresh(browser, "facts-forget", second);
        kept.StatusCode.Should().Be(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_grant_saved_before_Koan_recorded_its_facts_refreshes_and_revokes_but_remembers_no_consent()
    {
        const string person = "facts-older-person";
        await RegisterCodeClient("facts-older");
        var resource = Resource;

        // The rows as Koan stored them before a grant recorded its time of issue and client, and before consent was
        // remembered apart from grants.
        var older = await new AgentGrant
        {
            Subject = person,
            Capability = OAuthGrants.Capability("facts-older", ["mcp.read"]),
            Resource = resource,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        }.Save(Ct);
        var raw = OpaqueToken.New();
        await new RefreshToken
        {
            Id = OAuthGrants.HashToken(raw), FamilyId = OpaqueToken.New(), ClientId = "facts-older", Resource = resource,
            Subject = person, GrantedScopes = ["mcp.read"], GrantId = older.Id, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
        }.Save(Ct);
        (await AgentGrant.Get(older.Id, Ct))!.IssuedAt.Should().BeNull("an older grant answers null for its time of issue");

        // Refreshes: the refresh grant reads it live.
        using var browser = _fx.NewClient();
        var refreshed = await Refresh(browser, "facts-older", raw);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync(Ct));
        var next = await RefreshTokenOf(refreshed);

        // Remembers no consent: the person is asked again, so no unlisted grant keeps them remembered.
        (await browser.GetAsync($"/test/signin?sub={person}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, challenge) = OAuthTestFlows.Pkce();
        var authorize = await browser.GetAsync(Authorize("facts-older", challenge), Ct);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authorize.Headers.Location!.ToString().Should().NotStartWith(Redirect, "an older grant does not skip the consent page");
        (await AgentGrant.Approvals(person, 10, Ct)).Should().BeEmpty("an older grant is not listed");

        // Revokes: removing it fails its next refresh closed.
        (await older.Remove(Ct)).Should().BeTrue();
        var revoked = await Refresh(browser, "facts-older", next);
        revoked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await revoked.Content.ReadAsStringAsync(Ct)).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Expired_approvals_are_swept_and_live_reads_skip_the_expired_rows()
    {
        const string person = "facts-sweep-person";
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 40; i++)
            await new AgentGrant
            {
                Subject = person, Capability = "has:scope:expired", Resource = "Notes",
                IssuedAt = now.AddDays(-40), ExpiresAt = now.AddDays(-10), ClientId = "facts-sweep",
            }.Save(Ct);
        var live = await new AgentGrant
        {
            Subject = person, Capability = "has:scope:notes", Resource = "Notes",
            IssuedAt = now.AddDays(-1), ExpiresAt = now.AddDays(29), ClientId = "facts-sweep",
        }.Save(Ct);
        var lent = await new AgentGrant { Subject = person, Capability = "is:auditor", Resource = "Notes", ExpiresAt = now.AddDays(-1) }.Save(Ct);
        var expiredConsent = await new RememberedConsent { Id = "facts-sweep-consent", ExpiresUtc = now.AddDays(-1) }.Save(Ct);

        // The gate's lookup and the approvals read find the live grant among many expired ones.
        (await new AgentGrantStore().ActiveCapabilities(person, "Notes", Ct)).Should().Equal(["has:scope:notes"]);
        (await AgentGrant.Approvals(person, 1, Ct)).Select(g => g.Id).Should().Equal([live.Id]);

        await OAuthArtifactCleanupService.Sweep(_fx.Services.GetRequiredService<Koan.Data.Abstractions.IMaintenanceScopes>(), now, Ct);

        (await AgentGrant.Query(g => g.Subject == person, Ct)).Select(g => g.Id).Should()
            .BeEquivalentTo([live.Id, lent.Id], "the sweep removes the expired grants a client was issued, and no other");
        (await RememberedConsent.Get(expiredConsent.Id, Ct)).Should().BeNull("expired remembered consent is swept");
    }

    private static Task RegisterCodeClient(string clientId)
        => new OAuthClient { Id = clientId, ClientName = "Code Client", RedirectUris = [Redirect], CreatedUtc = DateTimeOffset.UtcNow }.Save(Ct);

    /// <summary>One code-flow connection in a signed-in browser: answers its refresh token and whether the person was
    /// asked on the consent page (true) or remembered (false).</summary>
    private async Task<(string Refresh, bool Asked)> Connect(HttpClient browser, string clientId)
    {
        var (verifier, challenge) = OAuthTestFlows.Pkce();
        var authorize = await browser.GetAsync(Authorize(clientId, challenge), Ct);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = authorize.Headers.Location!.ToString();
        var asked = !location.StartsWith(Redirect, StringComparison.Ordinal);
        if (asked)
        {
            using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{OAuthTestFlows.Query(location, "rid")}/approve");
            approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var approved = JsonDocument.Parse(await (await browser.SendAsync(approve, Ct)).Content.ReadAsStringAsync(Ct));
            location = approved.RootElement.GetProperty("redirect").GetString()!;
        }
        var (_, refresh) = await OAuthTestFlows.RedeemBoth(browser, "", clientId, OAuthTestFlows.Query(location, "code"), verifier, Redirect);
        return (refresh, asked);
    }

    private string Authorize(string clientId, string challenge)
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = Redirect,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "mcp.read",
            ["resource"] = Resource,
        });

    private static Task RegisterDeviceClient(string clientId, string name)
        => new OAuthClient
        {
            Id = clientId,
            ClientName = name,
            GrantTypes = [OAuthGrantTypes.DeviceCode],
            CreatedUtc = DateTimeOffset.UtcNow,
        }.Save(Ct);

    /// <summary>Device request → the person approves → poll; answers the refresh token.</summary>
    private async Task<string> DeviceFlow(string clientId, string subject)
    {
        using var device = _fx.NewClient();
        using var start = await device.PostAsync("/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "mcp.read",
            ["resource"] = Resource,
        }), Ct);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync(Ct));
        using var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync(Ct));
        var deviceCode = started.RootElement.GetProperty("device_code").GetString()!;
        var userCode = started.RootElement.GetProperty("user_code").GetString()!;

        using (var person = _fx.NewClient())
        {
            (await person.GetAsync($"/test/signin?sub={subject}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{userCode}/approve");
            approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            (await person.SendAsync(approve, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var poll = await device.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = OAuthGrantTypes.DeviceCode,
            ["device_code"] = deviceCode,
            ["client_id"] = clientId,
        }), Ct);
        poll.StatusCode.Should().Be(HttpStatusCode.OK, await poll.Content.ReadAsStringAsync(Ct));
        return await RefreshTokenOf(poll);
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient http, string clientId, string refreshToken)
        => http.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = OAuthGrantTypes.RefreshToken,
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }), Ct);

    private static async Task<string> RefreshTokenOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("refresh_token").GetString()!;
    }

    /// <summary>The grant a refresh token is backed by, read through the token's stored row.</summary>
    private static async Task<AgentGrant> GrantBehind(string refreshToken)
    {
        var row = await RefreshToken.Get(OAuthGrants.HashToken(refreshToken), Ct);
        row.Should().NotBeNull();
        var grant = await AgentGrant.Get(row!.GrantId, Ct);
        grant.Should().NotBeNull();
        return grant!;
    }
}
