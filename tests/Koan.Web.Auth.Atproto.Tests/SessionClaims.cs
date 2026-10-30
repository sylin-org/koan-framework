using System.Diagnostics;
using System.Net;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// A claim on a session is one write of a fixed lease: one process presents the refresh token at a time, a holder that
/// stopped keeps others out no longer than the lease, a result lands only over the grant it refreshed from, and a store
/// that fails the result fails that request and the next one keeps the session. Ended and expired sessions are pruned on a
/// store without conditional deletion too, and a disconnect that ended nothing says so.
/// </summary>
public sealed class SessionClaims
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpStatusCode> Send(Node node, string did)
    {
        using var response = await node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);
        return response.StatusCode;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_store_that_fails_the_refreshed_write_fails_that_request_and_the_next_keeps_the_session(bool afterItLands)
    {
        StoreFaults.Reset();
        await using var world = await SessionWorld.Start(accounts: 1, declare: StoreFaults.Declare);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        if (afterItLands) Volatile.Write(ref StoreFaults.FailAfterResults, 1);
        else Volatile.Write(ref StoreFaults.FailResults, 1);

        await Assert.ThrowsAsync<TimeoutException>(() => Send(node, did));
        Assert.Equal(HttpStatusCode.OK, await Send(node, did));

        // Before it lands, the next request stores the tokens this process holds; after, it reads the stored grant.
        Assert.Equal(1, world.Pds.Refreshes);
        var stored = await node.Store.Read(did, Ct);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
        Assert.Equal(world.Pds.CurrentRefresh(did), stored!.Grant!.TokenSet.RefreshToken);
        Assert.Null(stored.Row.ClaimedUntil);
    }

    [Fact]
    public async Task A_waiter_takes_the_expired_claim_of_a_holder_that_stopped_and_that_holders_writes_are_refused()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        // A process claimed the grant for a second and stopped.
        var abandoned = await node.Store.Claim((await node.Store.Read(did, Ct))!, TimeSpan.FromSeconds(1), Ct);
        Assert.NotNull(abandoned);
        var clock = Stopwatch.StartNew();

        Assert.Equal(HttpStatusCode.OK, await Send(node, did));

        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3));
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.False(await abandoned!.End());
        Assert.Null(await abandoned.Refreshed(SessionRows.Grant(did)));
        Assert.Equal(world.Pds.CurrentRefresh(did), (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Fact]
    public async Task A_waiter_behind_a_live_claim_fails_after_one_request_timeout_and_the_holders_result_lands()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var holder = world.Node();
        var waiter = world.Node(options => options.RequestTimeoutSeconds = 1);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        // The holder's refresh takes three seconds at the provider, past the waiter's request timeout of one.
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(3);

        var first = Send(holder, did);
        AtprotoSession? claimed = null;
        while (claimed?.ClaimedUntil is null) { await Task.Delay(5, Ct); claimed = await AtprotoSession.Get(AtprotoSession.KeyFor(did), Ct); }
        var clock = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<TimeoutException>(() => Send(waiter, did));

        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(2));
        Assert.Equal("Another request is refreshing this AT sign-in; try again.", failure.Message);
        Assert.Equal(HttpStatusCode.OK, await first);
        Assert.Equal(1, world.Pds.Refreshes);
        var stored = (await holder.Store.Read(did, Ct))!;
        Assert.Equal(world.Pds.CurrentRefresh(did), stored.Grant!.TokenSet.RefreshToken);
        Assert.Null(stored.Row.ClaimedUntil);
    }

    [Fact]
    public async Task A_claim_that_settles_nothing_is_released_and_the_grant_stays()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        var read = (await node.Store.Read(did, Ct))!;

        var claim = await node.Store.Claim(read, TimeSpan.FromMinutes(1), Ct);
        Assert.True((await node.Store.Read(did, Ct))!.Claimed);
        await claim!.DisposeAsync();

        var row = (await node.Store.Read(did, Ct))!;
        Assert.Null(row.Row.ClaimedUntil);
        Assert.Equal(read.Row.Grant, row.Row.Grant);
    }

    [Fact]
    public async Task A_late_result_over_a_taken_claim_stands_and_the_takers_release_is_refused()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        var holder = await node.Store.Claim((await node.Store.Read(did, Ct))!, TimeSpan.FromMilliseconds(100), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(150), Ct);
        var taker = await node.Store.Claim((await node.Store.Read(did, Ct))!, TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(taker);

        var result = await holder!.Refreshed(SessionRows.Grant(did));
        await taker!.DisposeAsync();

        var row = (await node.Store.Read(did, Ct))!;
        Assert.Equal(result!.Row.Version, row.Row.Version);
        Assert.Null(row.Row.ClaimedUntil);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("duckdb")]
    public async Task Two_holders_refreshing_from_one_grant_store_one_result(string adapter)
    {
        await using var world = await SessionWorld.Start(accounts: 1, adapter);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        var first = await node.Store.Claim((await node.Store.Read(did, Ct))!, TimeSpan.FromMilliseconds(100), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(150), Ct);
        var second = await node.Store.Claim((await node.Store.Read(did, Ct))!, TimeSpan.FromMinutes(1), Ct);

        var secondResult = await second!.Refreshed(SessionRows.Grant(did));
        var firstResult = await first!.Refreshed(SessionRows.Grant(did));

        Assert.NotNull(secondResult);
        Assert.Null(firstResult);
        var row = (await node.Store.Read(did, Ct))!;
        Assert.Equal(secondResult.Row.Version, row.Row.Version);
        Assert.Null(row.Row.ClaimedUntil);
    }

    [Fact]
    public async Task A_disconnect_whose_end_fails_says_so_and_the_new_sign_in_stands()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        world.Pds.RevokeHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnect = node.Sessions.Disconnect(did, Ct);
        await world.Pds.RevokeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The person signs in again in another process while the revocation is in flight.
        await world.SignIn(world.Node(), did);
        world.Pds.RevokeHold.SetResult();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => disconnect);
        Assert.Contains("was not ended", failure.Message);
        Assert.NotNull((await node.Store.Read(did, Ct))!.Grant);
    }

    [Fact]
    public async Task Ended_and_expired_sessions_are_pruned_on_a_store_without_conditional_delete()
    {
        await using var world = await SessionWorld.Start(accounts: 2, adapter: "duckdb");
        var (ended, expired) = (world.Dids[0], world.Dids[1]);
        var node = world.Node();
        Assert.False(Data<AtprotoSession, string>.Capabilities.Has(DataCaps.Write.ConditionalDelete));
        await world.SignIn(node, ended);
        await world.SignIn(node, expired);
        using (var response = await node.Sessions.Send(ended, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await node.Sessions.Disconnect(ended, Ct);
        var emptied = await AtprotoSession.Get(AtprotoSession.KeyFor(ended), Ct);
        Assert.Null(emptied!.Grant);
        Assert.NotNull(emptied.ExpiresAt);
        var lapsed = await AtprotoSession.Get(AtprotoSession.KeyFor(expired), Ct);
        lapsed!.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await lapsed.Save(Ct);

        Assert.Equal(2, await node.Store.Sweep(DateTimeOffset.UtcNow.AddSeconds(1), pageSize: 1, Ct));

        Assert.Null(await AtprotoSession.Get(AtprotoSession.KeyFor(ended), Ct));
        Assert.Null(await AtprotoSession.Get(AtprotoSession.KeyFor(expired), Ct));
        await world.SignIn(node, ended);
        Assert.NotNull(await node.Sessions.GetSessionMetadata(ended, Ct));
    }
}
