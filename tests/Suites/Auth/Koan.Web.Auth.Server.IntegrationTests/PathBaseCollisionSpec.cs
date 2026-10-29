using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — a path Koan composes from its own routes gets the path base exactly once, even when the path base is the
/// route's own first segment: under <c>/oauth</c> approve is <c>/oauth/oauth/request/{rid}/approve</c>, under
/// <c>/auth</c> the provider's challenge link is <c>/auth/auth/corp/challenge</c>, and each works.
/// </summary>
public abstract class PathBaseCollisionSpec<TFixture>(TFixture fx) where TFixture : BasePathFixture
{
    private const string Redirect = "http://127.0.0.1:7871/cb";

    private async Task<(HttpClient Browser, string Rid)> PendingRequest(string clientId)
    {
        await RegisterCodeClient(fx.Services, clientId, Redirect);
        var browser = fx.NewClient();
        (await browser.GetAsync($"{fx.PathBase}/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var authorize = await browser.GetAsync(Authorize(fx.PathBase, clientId, Redirect, Pkce().Challenge, fx.ApplicationUrl), Ct);
        var consent = authorize.Headers.Location!.ToString();
        consent.Should().StartWith($"{fx.PathBase}/consent?rid=", "the consent page gets the base once");
        return (browser, Query(consent, "rid"));
    }

    private async Task<JsonElement> Page(HttpClient browser, string rid)
    {
        using var res = await browser.GetAsync($"{fx.PathBase}/consent?rid={rid}", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Approve_and_deny_paths_carry_the_base_once_and_work()
    {
        var (approver, approveRid) = await PendingRequest(typeof(TFixture).Name + "-approve");
        using var _ = approver;
        var approve = (await Page(approver, approveRid)).GetProperty("approve").GetString();
        approve.Should().Be($"{fx.PathBase}/oauth/request/{approveRid}/approve");
        using (var approved = await approver.PostAsync(approve, new FormUrlEncodedContent([]), Ct))
        {
            approved.StatusCode.Should().Be(HttpStatusCode.Redirect);
            Query(approved.Headers.Location!.ToString(), "code").Should().NotBeEmpty();
        }

        var (denier, denyRid) = await PendingRequest(typeof(TFixture).Name + "-deny");
        using var __ = denier;
        var deny = (await Page(denier, denyRid)).GetProperty("deny").GetString();
        deny.Should().Be($"{fx.PathBase}/oauth/request/{denyRid}/deny");
        using var denied = await denier.PostAsync(deny, new FormUrlEncodedContent([]), Ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Query(denied.Headers.Location!.ToString(), "error").Should().Be("access_denied");
    }

    [Fact]
    public async Task The_provider_challenge_link_carries_the_base_once_and_works()
    {
        var (browser, rid) = await PendingRequest(typeof(TFixture).Name + "-challenge");
        using var _ = browser;
        using var seam = await browser.GetAsync($"{fx.PathBase}/oauth/request/{rid}", Ct);
        using var doc = JsonDocument.Parse(await seam.Content.ReadAsStringAsync(Ct));
        var challenge = doc.RootElement.GetProperty("providers").EnumerateArray()
            .Single(provider => provider.GetProperty("id").GetString() == "corp").GetProperty("challengeUrl").GetString();

        challenge.Should().Be($"{fx.PathBase}/auth/corp/challenge");
        using var challenged = await browser.GetAsync(challenge, Ct);
        challenged.StatusCode.Should().Be(HttpStatusCode.Redirect);
        challenged.Headers.Location!.ToString().Should().StartWith("https://idp.example/authorize");
    }
}

/// <summary>SEC-0027 — the collision rule under <c>/oauth</c>.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class OAuthBasePathCollisionSpec(OAuthBasePathFixture fx)
    : PathBaseCollisionSpec<OAuthBasePathFixture>(fx), IClassFixture<OAuthBasePathFixture>;

/// <summary>SEC-0027 — the collision rule under <c>/auth</c>.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class AuthBasePathCollisionSpec(AuthBasePathFixture fx)
    : PathBaseCollisionSpec<AuthBasePathFixture>(fx), IClassFixture<AuthBasePathFixture>;
