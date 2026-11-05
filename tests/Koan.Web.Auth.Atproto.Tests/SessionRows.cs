using System.Net;
using System.Security.Claims;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// AT sessions and pending authorizations are rows in the application's own store. Two processes over one store sign
/// people in alike and read each other's sessions; one DID's grant is refreshed by one process at a time, so the newer
/// tokens stand; a row this application cannot read fails only its own DID; and what expired is swept a page at a time.
/// </summary>
public sealed class SessionRows
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_processes_on_one_store_each_sign_a_different_did_in_at_once_and_each_reads_the_others_session()
    {
        await using var world = await SessionWorld.Start(accounts: 2);
        var (alice, bob) = (world.Dids[0], world.Dids[1]);
        var one = world.Node();
        var two = world.Node();

        var signedIn = await Task.WhenAll(world.SignIn(one, alice), world.SignIn(two, bob));

        Assert.Equal([alice, bob], signedIn.Select(principal => principal.FindFirstValue(ClaimTypes.NameIdentifier)));
        Assert.Equal(bob, (await one.Sessions.GetSessionMetadata(bob, Ct))!.Did);
        Assert.Equal(alice, (await two.Sessions.GetSessionMetadata(alice, Ct))!.Did);
        using var fromOne = await one.Sessions.Send(bob, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);
        using var fromTwo = await two.Sessions.Send(alice, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);
        Assert.Equal(HttpStatusCode.OK, fromOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromTwo.StatusCode);
        var tables = await world.Store.Tables();
        Assert.Contains(tables, table => table.Contains(nameof(AtprotoSession), StringComparison.Ordinal));
        Assert.Contains(tables, table => table.Contains(nameof(AtprotoAuthorizationState), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_refreshes_of_one_did_on_two_processes_keep_the_newer_tokens()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        await world.SignIn(world.Node(), did);
        var one = world.Node();
        var two = world.Node();
        await world.ExpireAccess(one, did);
        // The provider rotates the refresh token as it answers and ends the grant when a rotated one is presented again.
        world.Pds.RefreshDelay = TimeSpan.FromMilliseconds(300);

        var sent = await Task.WhenAll(
            one.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct),
            two.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct));

        Assert.All(sent, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in sent) response.Dispose();
        Assert.Equal(1, world.Pds.Refreshes);
        var stored = await one.Store.Read(did, Ct);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
        Assert.Equal(world.Pds.CurrentRefresh(did), stored!.Grant!.TokenSet.RefreshToken);
        Assert.Null(stored.Row.ClaimedUntil);
    }

    [Fact]
    public async Task A_row_this_application_cannot_read_fails_only_its_own_did_and_the_person_signs_in_again()
    {
        await using var world = await SessionWorld.Start(accounts: 2);
        var (lost, kept) = (world.Dids[0], world.Dids[1]);
        var node = world.Node();
        await world.SignIn(node, lost);
        await world.SignIn(node, kept);
        // Only the first DID's grant is protected under a key this application no longer has.
        var row = await AtprotoSession.Get(AtprotoSession.KeyFor(lost), Ct);
        row!.Grant = world.Store.LostRing().CreateProtector(Constants.SessionProtectionPurpose).Protect("{}");
        await row.Save(Ct);
        var later = world.Node();

        Assert.Equal(kept, (await later.Sessions.GetSessionMetadata(kept, Ct))!.Did);
        using (var response = await later.Sessions.Send(kept, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await later.Sessions.GetSessionMetadata(lost, Ct));
        var send = await Assert.ThrowsAsync<AtprotoAuthorizationRequiredException>(() =>
            later.Sessions.Send(lost, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct));
        Assert.Equal("AT authorization is required for this DID.", send.Message);

        await world.SignIn(later, lost);
        Assert.Equal(lost, (await later.Sessions.GetSessionMetadata(lost, Ct))!.Did);
        Assert.Equal(kept, (await world.Node().Sessions.GetSessionMetadata(kept, Ct))!.Did);
    }

    [Fact]
    public async Task Expired_states_and_sessions_are_swept_a_page_at_a_time_and_live_ones_stay()
    {
        await using var store = await TestStore.Start();
        var sessions = new AtprotoSessionStore(store.Protection());
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 23; i++) await sessions.StoreAsync("expired-" + i, new OAuthStateData { ExpiresAt = now.AddMinutes(-1) }, Ct);
        await sessions.StoreAsync("live", new OAuthStateData { ExpiresAt = now.AddMinutes(5), AppState = "live" }, Ct);
        for (var i = 0; i < 17; i++)
            await new AtprotoSession { Id = AtprotoSession.KeyFor("did:plc:expired" + i), Did = "did:plc:expired" + i, Version = "v", Grant = "g", ExpiresAt = now.AddDays(-1) }.Save(Ct);
        var live = SessionWorld.Did(1);
        await sessions.Commit(live, Grant(live), null, Ct);

        var removed = await sessions.Sweep(now, pageSize: 5, Ct);

        Assert.Equal(23 + 17, removed);
        for (var i = 0; i < 23; i++) Assert.Null(await AtprotoAuthorizationState.Get(AtprotoAuthorizationState.KeyFor("expired-" + i), Ct));
        for (var i = 0; i < 17; i++) Assert.Null(await AtprotoSession.Get(AtprotoSession.KeyFor("did:plc:expired" + i), Ct));
        Assert.Equal("live", (await sessions.Peek("live", Ct))!.AppState);
        Assert.NotNull((await sessions.Read(live, Ct))!.Grant);
        Assert.Equal(0, await sessions.Sweep(now, pageSize: 5, Ct));
    }

    [Fact]
    public async Task A_pending_authorization_is_read_only_while_live_and_consumed_once_across_processes()
    {
        await using var store = await TestStore.Start();
        var one = new AtprotoSessionStore(store.Protection());
        var two = new AtprotoSessionStore(store.Protection());
        await one.StoreAsync("state", new OAuthStateData { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1), AppState = "browser" }, Ct);
        await one.StoreAsync("old", new OAuthStateData { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1), AppState = "plaintext-of-the-old-state" }, Ct);

        Assert.Equal("browser", (await two.Peek("state", Ct))!.AppState);
        Assert.Null(await two.Peek("old", Ct));
        var consumed = await Task.WhenAll(one.ConsumeAsync("state", Ct), two.ConsumeAsync("state", Ct));
        Assert.Single(consumed, value => value?.AppState == "browser");
        Assert.Null(await one.ConsumeAsync("state", Ct));
        Assert.Null(await two.Peek("\ud800", Ct));
        var row = await AtprotoAuthorizationState.Get(AtprotoAuthorizationState.KeyFor("old"), Ct);
        Assert.DoesNotContain("plaintext-of-the-old-state", row!.Pending);
    }

    [Fact]
    public async Task A_grant_the_provider_ended_ends_the_session_at_its_next_refresh()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        world.Pds.End(did);

        var send = await Assert.ThrowsAsync<AtprotoAuthorizationRequiredException>(() =>
            node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct));

        Assert.Equal("AT authorization is required for this DID.", send.Message);
        Assert.Null((await node.Store.Read(did, Ct))?.Grant);
        Assert.Null(await node.Sessions.GetSessionMetadata(did, Ct));
        Assert.Equal(0, node.Sessions.ClientsHeld);
    }

    [Fact]
    public async Task Disconnect_revokes_the_grant_and_ends_the_session_for_every_process()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var one = world.Node();
        var two = world.Node();
        await world.SignIn(one, did);
        Assert.NotNull(await two.Sessions.GetSessionMetadata(did, Ct));

        await one.Sessions.Disconnect(did, Ct);

        Assert.Null(world.Pds.CurrentRefresh(did));
        Assert.Null(await one.Store.Read(did, Ct));
        Assert.Null(await two.Sessions.GetSessionMetadata(did, Ct));
        Assert.Equal(0, one.Sessions.ClientsHeld);
    }

    [Fact]
    public async Task A_sign_in_replaces_a_session_whose_refresh_another_process_holds_and_that_refresh_is_not_stored()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        var read = await node.Store.Read(did, Ct);
        await using var claim = await node.Store.Claim(read!, TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(claim);
        Assert.Null(await node.Store.Claim(read!, TimeSpan.FromMinutes(1), Ct));

        await world.SignIn(world.Node(), did);

        var signedIn = await node.Store.Read(did, Ct);
        Assert.Null(signedIn!.Row.ClaimedUntil);
        Assert.Null(await claim!.Refreshed(Grant(did)));
        Assert.Equal(signedIn.Row.Grant, (await node.Store.Read(did, Ct))!.Row.Grant);
        Assert.False(await claim.End());
        Assert.NotNull((await node.Store.Read(did, Ct))!.Grant);
    }

    internal static OAuthSessionData Grant(string did)
    {
        using var key = DPoPKeyPair.Generate();
        return new OAuthSessionData
        {
            ClientId = SessionWorld.ClientId, DPoPKey = key.ExportKeyPair(),
            TokenSet = new TokenSet { Sub = did, Scope = "atproto", AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
        };
    }
}
