using System;
using System.Collections.Generic;
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
/// A flow host, on its own in-memory store, whose application declares one more <see cref="AgentGrant"/> remove hook:
/// it cancels the remove's own token when a spec arms it. The application's hooks run after the modules' hooks.
/// </summary>
public sealed class CancelOnRemoveFixture : OAuthFlowFixture
{
    private CancellationTokenSource? _armed;

    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [new("Koan:Data:Sources:Default:ConnectionString", "memory://oauth-cancel-on-remove-tests")];

    protected override void Declare() => AgentGrant.Lifecycle.BeforeRemove(context =>
    {
        Interlocked.Exchange(ref _armed, null)?.Cancel();
        return context.Proceed();
    });

    /// <summary>Cancel <paramref name="source"/> once the next grant removal has run the modules' remove hooks.</summary>
    public void CancelNextRemove(CancellationTokenSource source) => _armed = source;
}

/// <summary>
/// SEC-0005 (amended 2026-10-30) — a revoke fails toward asking the person again: the remembered consent is forgotten
/// before the grant is deleted and with a token of its own, so a revoke whose caller's token is cancelled midway still
/// leaves no consent remembered.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class RevokeCancelledSpec : IClassFixture<CancelOnRemoveFixture>
{
    private readonly CancelOnRemoveFixture _fx;
    public RevokeCancelledSpec(CancelOnRemoveFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private const string Redirect = "http://127.0.0.1:7791/cb";
    private string Resource => $"{_fx.BaseUrl}/mcp";

    [Fact]
    public async Task A_revoke_whose_token_is_cancelled_after_the_hook_runs_still_leaves_no_consent()
    {
        await new OAuthClient { Id = "cancel-revoke", ClientName = "Client", RedirectUris = [Redirect], CreatedUtc = DateTimeOffset.UtcNow }.Save(Ct);
        using var browser = _fx.NewClient();
        (await browser.GetAsync("/test/signin?sub=cancel-revoke-person", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The person consents once; the connection's grant backs its refresh token.
        var (verifier, challenge) = OAuthTestFlows.Pkce();
        var first = await browser.GetAsync(OAuthTestFlows.Authorize("", "cancel-revoke", Redirect, challenge, Resource), Ct);
        var rid = OAuthTestFlows.Query(first.Headers.Location!.ToString(), "rid");
        rid.Should().NotBeEmpty("the first connection asks the person");
        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{rid}/approve");
        approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var approved = JsonDocument.Parse(await (await browser.SendAsync(approve, Ct)).Content.ReadAsStringAsync(Ct));
        var code = OAuthTestFlows.Query(approved.RootElement.GetProperty("redirect").GetString()!, "code");
        var (_, refresh) = await OAuthTestFlows.RedeemBoth(browser, "", "cancel-revoke", code, verifier, Redirect);
        var grant = (await AgentGrant.Get((await RefreshToken.Get(OAuthGrants.HashToken(refresh), Ct))!.GrantId, Ct))!;

        // The revoke's token is cancelled after the server's hook has run and before the grant is deleted.
        using var caller = new CancellationTokenSource();
        _fx.CancelNextRemove(caller);
        try { await grant.Remove(caller.Token); }
        catch (OperationCanceledException) { /* the delete saw the cancelled token */ }

        var (_, againChallenge) = OAuthTestFlows.Pkce();
        var next = await browser.GetAsync(OAuthTestFlows.Authorize("", "cancel-revoke", Redirect, againChallenge, Resource), Ct);
        next.StatusCode.Should().Be(HttpStatusCode.Redirect);
        next.Headers.Location!.ToString().Should().NotStartWith(Redirect, "a revoke that did not finish still forgets the consent");
        OAuthTestFlows.Query(next.Headers.Location!.ToString(), "rid").Should().NotBeEmpty("the consent page asks again");
    }
}
