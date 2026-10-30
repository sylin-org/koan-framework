using System.Net;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// The signed-in clients a process keeps are bounded: past capacity the one used least recently is evicted and
/// disposed, a client is never disposed under an operation that rents it, and a failed send disposes its client.
/// </summary>
public sealed class ClientCacheSpec
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Past_capacity_the_client_used_least_recently_is_evicted_and_disposed()
    {
        var cache = new ClientCache<Probe>(capacity: 2);
        var (a, b, c) = (new Probe(), new Probe(), new Probe());
        cache.Admit("a", a).Dispose();
        cache.Admit("b", b).Dispose();
        cache.TryRent("a", _ => true)!.Dispose();

        cache.Admit("c", c).Dispose();

        Assert.Equal(2, cache.Count);
        Assert.True(b.Disposed);
        Assert.False(a.Disposed);
        Assert.False(c.Disposed);
        Assert.Null(cache.TryRent("b", _ => true));
    }

    [Fact]
    public void A_client_evicted_while_rented_is_disposed_when_its_last_renter_returns_it()
    {
        var cache = new ClientCache<Probe>(capacity: 1);
        var (a, b) = (new Probe(), new Probe());
        var first = cache.Admit("a", a);
        var second = cache.TryRent("a", _ => true)!;

        cache.Admit("b", b).Dispose();

        Assert.False(a.Disposed);
        first.Dispose();
        Assert.False(a.Disposed);
        second.Dispose();
        Assert.True(a.Disposed);
        Assert.False(b.Disposed);
    }

    [Fact]
    public void A_client_that_no_longer_fits_or_is_retired_leaves_the_cache_and_is_disposed()
    {
        var cache = new ClientCache<Probe>(capacity: 4);
        var (stale, failed) = (new Probe(), new Probe());
        cache.Admit("stale", stale).Dispose();
        var lease = cache.Admit("failed", failed);

        Assert.Null(cache.TryRent("stale", _ => false));
        lease.Retire();

        Assert.True(stale.Disposed);
        Assert.Equal(0, cache.Count);
        Assert.False(failed.Disposed);
        lease.Dispose();
        lease.Dispose();
        Assert.True(failed.Disposed);
        Assert.Equal(1, failed.Disposals);
    }

    [Fact]
    public async Task A_process_holds_no_more_signed_in_clients_than_its_capacity()
    {
        await using var world = await SessionWorld.Start(accounts: 5);
        var node = world.Node(options => options.CachedClients = 2);

        foreach (var did in world.Dids) await world.SignIn(node, did);
        foreach (var did in world.Dids)
        {
            using var response = await node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(2, node.Sessions.ClientsHeld);
    }

    [Fact]
    public async Task A_failed_send_disposes_its_client_and_the_next_send_builds_another()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        Assert.Equal(1, node.Sessions.ClientsHeld);

        world.Pds.DropXrpc = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct));

        Assert.Equal(0, node.Sessions.ClientsHeld);
        world.Pds.DropXrpc = false;
        using var response = await node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, node.Sessions.ClientsHeld);
    }

    private sealed class Probe : IDisposable
    {
        public int Disposals { get; private set; }
        public bool Disposed => Disposals > 0;
        public void Dispose() => Disposals++;
    }
}
