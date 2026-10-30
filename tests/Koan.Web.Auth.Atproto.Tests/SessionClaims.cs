using System.Diagnostics;
using System.Net;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// A claim on a session is a short lease its holder renews while it works, fenced by its identity: a holder that stopped
/// keeps others waiting no longer than the lease, and its writes after another took over are refused. The rotated tokens a
/// refresh returns are written until they land. Ended and expired sessions are pruned on a store without conditional
/// deletion too, and a disconnect that ended nothing says so.
/// </summary>
public sealed class SessionClaims
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_store_that_fails_once_on_the_refreshed_write_still_keeps_the_rotated_tokens(bool afterItLands)
    {
        StoreFaults.Reset();
        await using var world = await SessionWorld.Start(accounts: 1, declare: StoreFaults.Declare);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        if (afterItLands) Volatile.Write(ref StoreFaults.FailAfterResults, 1);
        else Volatile.Write(ref StoreFaults.FailResults, 1);

        using var response = await node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, world.Pds.Refreshes);
        var stored = await node.Store.Read(did, Ct);
        Assert.Equal(world.Pds.CurrentRefresh(did), stored!.Grant!.TokenSet.RefreshToken);
        Assert.Null(stored.Row.ClaimedUntil);
        // Before it lands: one failed write and one that landed. After it lands: the one write, recognized by its version.
        if (afterItLands) Assert.Equal(0, Volatile.Read(ref StoreFaults.FailAfterResults));
        else Assert.Equal(-1, Volatile.Read(ref StoreFaults.FailResults));
    }

    [Fact]
    public async Task A_waiter_takes_over_within_the_lease_lifetime_of_a_holder_that_stopped()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        // A process claimed the grant and stopped: its lease is a second, and it never renews or releases it.
        var stopped = world.Node(claimLifetime: TimeSpan.FromSeconds(1), claimRenewal: TimeSpan.FromHours(1));
        var abandoned = await stopped.Store.Claim((await stopped.Store.Read(did, Ct))!, Ct);
        Assert.NotNull(abandoned);
        var clock = Stopwatch.StartNew();

        using var response = await node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3));
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.Equal(world.Pds.CurrentRefresh(did), (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
        Assert.False(await abandoned!.End());
        Assert.Null(await abandoned.Refreshed(SessionRows.Grant(did)));
        Assert.Equal(world.Pds.CurrentRefresh(did), (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
        await abandoned.DisposeAsync();
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
