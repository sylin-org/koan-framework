using System.Net;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// The interleavings a claim must survive, over SQLite, with leases of two seconds renewed every half second. A request
/// starts only with half a lease left, and once sent runs to its answer, its result landing while the row holds the grant
/// it refreshed from, over a takeover's claim too, and never over a newer grant.
/// No write is cut off short of its deadline, renewals landing out of order only extend the lease, a renewal never
/// restores the grant the result replaced, a result write that stalls within its bound never stops renewals, a renewal
/// hung through a short outage is renewed once the store is back, a result that landed is reported stored, one still
/// pending is reported unknown, a store cut shorter than the lease costs one refresh and no grant, a slow store
/// answers or fails to be retried, never ending the grant, and a waiter gives up after one request timeout without
/// taking over from a working holder.
/// </summary>
public sealed class ClaimInterleavings
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Renewal = TimeSpan.FromMilliseconds(500);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SessionWorld> World(string adapter = "sqlite")
    {
        StoreFaults.Reset();
        return await SessionWorld.Start(accounts: 1, adapter, StoreFaults.Declare);
    }

    private static Node Process(SessionWorld world) => world.Node(claimLifetime: Lifetime, claimRenewal: Renewal);

    private static async Task<HttpStatusCode> Send(Node node, string did)
    {
        using var response = await node.Sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: Ct);
        return response.StatusCode;
    }

    // A send's status, or the name of the exception it failed with.
    private static async Task<string> Outcome(Node node, string did)
    {
        try { return (await Send(node, did)).ToString(); }
        catch (Exception error) { return error.GetType().Name; }
    }

    private static void For(TimeSpan span, Action end) => _ = Task.Run(async () => { await Task.Delay(span); end(); });

    [Fact]
    public async Task Renewals_that_fail_while_a_refresh_is_at_the_provider_and_then_recover_still_store_that_refresh()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(3);
        // Renewals fail past half the lease, then recover while the refresh is still at the provider.
        StoreFaults.FailRenewals = true;
        For(TimeSpan.FromSeconds(1.8), () => StoreFaults.FailRenewals = false);

        Assert.Equal(HttpStatusCode.OK, await Send(node, did));

        Assert.Equal(1, world.Pds.Refreshes);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
        Assert.Equal(world.Pds.CurrentRefresh(did), (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_renewal_that_lands_unrecognized_past_its_bound_never_costs_the_result(bool slowReadBack)
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        Volatile.Write(ref StoreFaults.LandThenFailRenewal, 1);
        StoreFaults.SlowReadsAfterRenewal = slowReadBack;
        await using var claim = (await node.Store.Claim((await node.Store.Read(did, Ct))!, Ct))!;
        await Task.Delay(TimeSpan.FromMilliseconds(600), Ct);

        var stored = await claim.Refreshed(Grant(did, "rotated"));

        Assert.NotNull(stored);
        var row = (await node.Store.Read(did, Ct))!;
        Assert.Equal("rotated", row.Grant!.TokenSet.RefreshToken);
        Assert.Null(row.Row.ClaimId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_claim_starts_no_request_with_less_than_half_its_lease_left_whether_renewals_fail_fast_or_stall(bool stall)
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        StoreFaults.FailRenewals = !stall;
        StoreFaults.StallRenewals = stall;
        await using var claim = (await node.Store.Claim((await node.Store.Read(did, Ct))!, Ct))!;
        var half = claim.Held.Row.ClaimedUntil!.Value - Lifetime / 2;
        var whole = await claim.Begin();

        await Task.Delay(half + TimeSpan.FromMilliseconds(50) - DateTimeOffset.UtcNow, Ct);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var begun = await claim.Begin();
        var waited = clock.Elapsed;
        StoreFaults.FailRenewals = false;
        StoreFaults.StallRenewals = false;

        Assert.True(whole, "A request starts with the whole lease left.");
        Assert.False(begun, "No request starts with less than half the lease left.");
        // It waits an interval for a renewal to give it room, then gives up.
        Assert.InRange(waited, Renewal - TimeSpan.FromMilliseconds(30), Renewal + TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task A_claim_that_lands_with_less_than_half_its_lease_left_renews_before_its_request_starts()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        // The claim's own write takes 1.1 s of its 2 s lease; renewals are fast.
        Volatile.Write(ref StoreFaults.ClaimDelayMs, 1100);

        Assert.Equal(HttpStatusCode.OK, await Send(node, did));

        var renewed = Interlocked.Read(ref StoreFaults.FirstRenewalLandedTicks);
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.NotEqual(0, renewed);
        Assert.True(world.Pds.RefreshedAt > new DateTimeOffset(renewed, TimeSpan.Zero), "The refresh started once a renewal gave it room.");
        Assert.Equal(world.Pds.CurrentRefresh(did), (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Fact]
    public async Task A_holder_that_cannot_renew_starts_no_new_request_and_with_no_takeover_its_request_in_flight_lands()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(3);
        // No renewal lands at all: the claim is lost at its mark and its lease lapses while the refresh is at the provider.
        StoreFaults.FailRenewals = true;

        Assert.Equal(HttpStatusCode.OK, await Send(node, did));
        StoreFaults.FailRenewals = false;

        Assert.Equal(1, world.Pds.Refreshes);
        var stored = (await node.Store.Read(did, Ct))!;
        Assert.Equal(world.Pds.CurrentRefresh(did), stored.Grant!.TokenSet.RefreshToken);
        Assert.Null(stored.Row.ClaimId);
    }

    [Fact]
    public async Task A_store_cut_shorter_than_the_lease_with_a_second_process_sending_costs_one_refresh_and_no_grant()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var holder = Process(world);
        var waiter = Process(world);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(3);

        var first = Send(holder, did);
        await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        StoreFaults.FailRenewals = true;
        For(TimeSpan.FromSeconds(1.6), () => StoreFaults.FailRenewals = false);
        var second = Send(waiter, did);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await Task.WhenAll(first, second));
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
        Assert.Equal(world.Pds.CurrentRefresh(did), (await holder.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Fact]
    public async Task A_renewal_after_a_result_that_landed_unrecognized_never_restores_the_old_refresh_token()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        await using var claim = (await node.Store.Claim((await node.Store.Read(did, Ct))!, Ct))!;
        StoreFaults.ClaimedGrant = claim.Held.Row.Grant;
        // The result lands, its answer and read-back fail, renewals come due meanwhile, and the later attempts fail.
        Volatile.Write(ref StoreFaults.ResultLandsThenFails, 1);

        await Record.ExceptionAsync(() => claim.Refreshed(Grant(did, "rotated")));
        Volatile.Write(ref StoreFaults.ResultLandsThenFails, 0);
        await Task.Delay(Renewal * 2, Ct);

        var row = (await node.Store.Read(did, Ct))!;
        Assert.Equal("rotated", row.Grant!.TokenSet.RefreshToken);
        Assert.Null(row.Row.ClaimId);
    }

    [Fact]
    public async Task A_result_write_that_stalls_within_its_bound_never_stops_renewals_and_the_waiter_waits_and_reads_it()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var holder = Process(world);
        var waiter = world.Node(options => options.RequestTimeoutSeconds = 2, Lifetime, Renewal);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(1.5);
        // The provider answers at 1.5 s; the store stalls the result until 3 s, past the claim's first lease, within the
        // result's window of a lease from 1.5 s. The waiter comes after that first lease would have ended, and waits up to
        // its request timeout of 2 s.
        StoreFaults.StallResultsFor(TimeSpan.FromSeconds(3));
        var first = Send(holder, did);
        await Task.Delay(Lifetime + TimeSpan.FromMilliseconds(300), Ct);
        var read = (await waiter.Store.Read(did, Ct))!;
        var second = Send(waiter, did);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await Task.WhenAll(first, second));
        Assert.True(read.Claimed, "Renewals went on while the result write stalled.");
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.Equal(world.Pds.CurrentRefresh(did), (await holder.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Fact]
    public async Task A_result_write_stalled_past_its_bound_is_given_up_within_it_and_the_waiter_goes_on()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var holder = Process(world);
        var waiter = world.Node(options => options.RequestTimeoutSeconds = 3, Lifetime, Renewal);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        await using var claim = (await holder.Store.Claim((await holder.Store.Read(did, Ct))!, Ct))!;
        StoreFaults.ClaimedGrant = claim.Held.Row.Grant;
        Volatile.Write(ref StoreFaults.StallResultsMs, 9000);

        var result = claim.Refreshed(Grant(did, "rotated"));
        // The stall is the holder's store trouble; once it gives up, the waiter's own writes go through.
        _ = result.ContinueWith(_ => Volatile.Write(ref StoreFaults.StallResultsMs, 0), TaskScheduler.Default);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var answer = await Send(waiter, did);
        var waited = clock.Elapsed;

        Assert.Equal(HttpStatusCode.OK, answer);
        // The holder gives its result up at the end of its window of a lease and releases; the waiter, which waits up to
        // its request timeout of 3 s, claims and refreshes itself.
        Assert.InRange(waited, TimeSpan.Zero, Lifetime + Renewal * 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => result);
        Assert.Equal(1, world.Pds.Refreshes);
    }

    [Fact]
    public async Task A_waiter_behind_a_holder_working_past_one_request_timeout_fails_to_be_retried_then_and_the_holders_result_lands()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var holder = Process(world);
        var log = new ErrorLog();
        var waiter = world.Node(options => options.RequestTimeoutSeconds = 1, Lifetime, Renewal, log);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        // The holder's refresh takes three seconds at the provider, past the waiter's request timeout of one.
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(3);

        var first = Send(holder, did);
        AtprotoSession? claimed = null;
        while (claimed?.ClaimId is null) { await Task.Delay(5, Ct); claimed = await AtprotoSession.Get(AtprotoSession.KeyFor(did), Ct); }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<TimeoutException>(() => Send(waiter, did));
        var waited = clock.Elapsed;

        Assert.InRange(waited, TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(500));
        Assert.Equal("Another request is refreshing this AT sign-in; try again.", failure.Message);
        Assert.Contains(log.Information, line => line.Contains(claimed.Id, StringComparison.Ordinal));
        Assert.Empty(log.Warnings);
        Assert.Empty(log.Errors);
        Assert.Equal(HttpStatusCode.OK, await first);
        Assert.Equal(1, world.Pds.Refreshes);
        var stored = (await holder.Store.Read(did, Ct))!;
        Assert.Equal(world.Pds.CurrentRefresh(did), stored.Grant!.TokenSet.RefreshToken);
        Assert.Null(stored.Row.ClaimId);
    }

    [Fact]
    public async Task A_result_that_landed_before_the_store_went_down_is_stored_and_the_call_answers()
    {
        StoreFaults.Reset();
        await using var world = await SessionWorld.Start(accounts: 1, declare: StoreFaults.Declare);
        var did = world.Dids[0];
        // A lease long enough that the result's attempts outlast a six-second outage.
        var node = world.Node(claimLifetime: TimeSpan.FromSeconds(9), claimRenewal: TimeSpan.FromSeconds(3));
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        Volatile.Write(ref StoreFaults.DownAfterLandingMs, 6000);

        Assert.Equal(HttpStatusCode.OK, await Send(node, did));

        Assert.Equal(1, world.Pds.Refreshes);
        var stored = (await node.Store.Read(did, Ct))!;
        Assert.Equal(world.Pds.CurrentRefresh(did), stored.Grant!.TokenSet.RefreshToken);
        Assert.Null(stored.Row.ClaimId);
    }

    [Fact]
    public async Task A_result_whose_release_is_refused_because_it_landed_is_reported_stored_without_an_error()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var log = new ErrorLog();
        var node = world.Node(claimLifetime: Lifetime, claimRenewal: Renewal, log: log);
        await world.SignIn(node, did);
        await using var claim = (await node.Store.Claim((await node.Store.Read(did, Ct))!, Ct))!;
        StoreFaults.ClaimedGrant = claim.Held.Row.Grant;
        // The first attempt lands and its answer fails; no read answers, and every later attempt fails, until the release.
        Volatile.Write(ref StoreFaults.FailAfterResults, 1);
        StoreFaults.FailResultsOnceLanded = true;
        StoreFaults.FailReadsUntilRelease = true;

        var stored = await claim.Refreshed(Grant(did, "rotated"));

        Assert.NotNull(stored);
        Assert.Empty(log.Errors);
        Assert.False(StoreFaults.FailReadsUntilRelease, "The release was attempted.");
        Assert.Equal("rotated", (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_renewal_hung_through_an_outage_of_little_more_than_an_interval_is_renewed_once_the_store_is_back(bool readsHang)
    {
        await using var world = await World();
        var did = world.Dids[0];
        // Production's ratio: a lease of three intervals.
        var lifetime = TimeSpan.FromSeconds(1.5);
        var holder = world.Node(claimLifetime: lifetime, claimRenewal: Renewal);
        var waiter = world.Node(claimLifetime: lifetime, claimRenewal: Renewal);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(3);

        var first = Send(holder, did);
        AtprotoSession? claimed = null;
        while (claimed?.ClaimId is null) { await Task.Delay(5, Ct); claimed = await AtprotoSession.Get(AtprotoSession.KeyFor(did), Ct); }
        while (world.Pds.Refreshes == 0) await Task.Delay(5, Ct);
        var until = claimed.ClaimedUntil!.Value;
        // From the claim until 80 ms past two intervals, renewals and reads hang (or reads fail at once), as on a dead
        // pooled connection after a failover. The store is back 420 ms before the lease ends.
        StoreFaults.StallRenewals = true;
        if (readsHang) StoreFaults.ReadsHang = true; else StoreFaults.ReadsFail = true;
        await Task.Delay(until - lifetime + Renewal * 2 + TimeSpan.FromMilliseconds(80) - DateTimeOffset.UtcNow, Ct);
        StoreFaults.StallRenewals = false;
        StoreFaults.ReadsHang = false;
        StoreFaults.ReadsFail = false;

        await Task.Delay(until + TimeSpan.FromMilliseconds(20) - DateTimeOffset.UtcNow, Ct);
        var row = (await AtprotoSession.Get(AtprotoSession.KeyFor(did), Ct))!;
        var second = Send(waiter, did);

        Assert.Equal(claimed.ClaimId, row.ClaimId);
        Assert.True(row.ClaimedUntil > until, "The lease was renewed once the store was back.");
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await Task.WhenAll(first, second));
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("duckdb")]
    public async Task Renewals_that_land_out_of_order_only_extend_the_lease_and_the_claim_holds(string adapter)
    {
        await using var world = await World(adapter);
        var did = world.Dids[0];
        var holder = Process(world);
        var waiter = Process(world);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(3);
        // The first renewal takes 600 ms, so the next lands first; the first would shorten the lease, and is refused.
        Volatile.Write(ref StoreFaults.SlowRenewalMs, 600);

        var first = Send(holder, did);
        await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        var second = Send(waiter, did);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], await Task.WhenAll(first, second));
        Assert.True(StoreFaults.SlowRenewalReached, "The slow renewal ran to the store.");
        Assert.False(StoreFaults.SlowRenewalLanded, "The slow renewal was refused by the longer lease.");
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
        Assert.Equal(world.Pds.CurrentRefresh(did), (await holder.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Fact]
    public async Task A_result_still_pending_in_a_store_that_ignores_cancellation_is_reported_unknown_and_lands_later()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var log = new ErrorLog();
        var node = world.Node(claimLifetime: Lifetime, claimRenewal: Renewal, log: log);
        await world.SignIn(node, did);
        var before = (await node.Store.Read(did, Ct))!;
        await using var claim = (await node.Store.Claim(before, Ct))!;
        StoreFaults.ClaimedGrant = claim.Held.Row.Grant;
        // The result and the release each take five seconds, past every bound, and the store makes them whatever the
        // caller's cancellation; renewals are fast.
        Volatile.Write(ref StoreFaults.IgnoreCancellationMs, 5000);
        Assert.True(await claim.Begin());

        await Assert.ThrowsAsync<TimeoutException>(() => claim.Refreshed(Grant(did, "rotated")));
        var reported = (await node.Store.Read(did, Ct))!;
        await Task.Delay(TimeSpan.FromSeconds(6), Ct);
        var later = (await node.Store.Read(did, Ct))!;

        Assert.Equal(before.Grant!.TokenSet.RefreshToken, reported.Grant!.TokenSet.RefreshToken);
        Assert.Empty(log.Errors);
        Assert.Contains(log.Warnings, warning => warning.Contains(reported.Row.Id, StringComparison.Ordinal));
        Assert.Equal("rotated", later.Grant!.TokenSet.RefreshToken);
        Assert.Null(later.Row.ClaimId);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(400)]
    [InlineData(700)]
    [InlineData(900)]
    [InlineData(1200)]
    [InlineData(1600)]
    public async Task A_store_whose_every_write_is_slow_answers_or_fails_to_be_retried_and_never_ends_the_grant(int ms)
    {
        await using var world = await World();
        var did = world.Dids[0];
        // The reviewer's scale: a request timeout of two seconds.
        var node = world.Node(options => options.RequestTimeoutSeconds = 2, Lifetime, Renewal);
        await world.SignIn(node, did);
        await world.ExpireAccess(node, did);
        Volatile.Write(ref StoreFaults.WriteDelayMs, ms);

        var first = await Outcome(node, did);
        await Task.Delay(Lifetime + Renewal, Ct);
        var second = await Outcome(node, did);
        Volatile.Write(ref StoreFaults.WriteDelayMs, 0);

        string[] outcomes = [first, second];
        TestContext.Current.TestOutputHelper?.WriteLine($"Writes of {ms} ms: first {first}, second {second}, {world.Pds.Refreshes} refreshes.");
        Assert.All(outcomes, outcome => Assert.Contains(outcome, new[] { nameof(HttpStatusCode.OK), nameof(TimeoutException) }));
        // Well within half a lease every send answers; past it, no refresh starts that the store could not record.
        if (ms <= 700) Assert.Equal([nameof(HttpStatusCode.OK), nameof(HttpStatusCode.OK)], outcomes);
        if (ms > Lifetime.TotalMilliseconds / 2) Assert.Equal(0, world.Pds.Refreshes);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
        Assert.Equal(world.Pds.CurrentRefresh(did), (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Fact]
    public async Task A_waiter_that_takes_over_a_lapsed_claim_without_room_leaves_the_holders_late_result_standing()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var holder = world.Node(options => options.RequestTimeoutSeconds = 5, Lifetime, Renewal);
        var waiter = world.Node(options => options.RequestTimeoutSeconds = 5, Lifetime, Renewal);
        await world.SignIn(holder, did);
        await world.ExpireAccess(holder, did);
        world.Pds.RefreshDelay = TimeSpan.FromSeconds(2.5);

        var first = Send(holder, did);
        AtprotoSession? claimed = null;
        while (claimed?.ClaimId is null) { await Task.Delay(5, Ct); claimed = await AtprotoSession.Get(AtprotoSession.KeyFor(did), Ct); }
        // Once the holder began, every write takes 1.6 s: no renewal lands, the lease lapses while the refresh is at the
        // provider, and the waiter, reading just after, takes the claim over with no room left to begin.
        await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);
        Volatile.Write(ref StoreFaults.WriteDelayMs, 1600);
        await Task.Delay(claimed.ClaimedUntil!.Value + TimeSpan.FromMilliseconds(20) - DateTimeOffset.UtcNow, Ct);
        var second = Send(waiter, did);

        var answers = await Task.WhenAll(first, second);
        Volatile.Write(ref StoreFaults.WriteDelayMs, 0);
        world.Pds.RefreshDelay = TimeSpan.Zero;
        await Task.Delay(Lifetime + Renewal, Ct);
        var later = await Send(holder, did);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], answers);
        // The waiter's takeover landed before the holder's result, which cleared it.
        var landings = StoreFaults.Landings.ToArray();
        var takeover = Array.FindIndex(landings, landing => landing.StartsWith("claim ", StringComparison.Ordinal) && landing != "claim " + claimed.ClaimId);
        Assert.InRange(takeover, 0, Array.IndexOf(landings, "result") - 1);
        Assert.Equal(HttpStatusCode.OK, later);
        Assert.Equal(1, world.Pds.Refreshes);
        Assert.NotNull(world.Pds.CurrentRefresh(did));
        Assert.Equal(world.Pds.CurrentRefresh(did), (await holder.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    [Fact]
    public async Task A_late_result_over_a_takeover_stands_and_the_takers_begin_and_release_are_refused()
    {
        await using var world = await World();
        var did = world.Dids[0];
        // The holder's lease is a second and it never renews; the taker's is the usual two seconds.
        var stopped = world.Node(claimLifetime: TimeSpan.FromSeconds(1), claimRenewal: TimeSpan.FromHours(1));
        var other = Process(world);
        await world.SignIn(stopped, did);
        await using var holder = (await stopped.Store.Claim((await stopped.Store.Read(did, Ct))!, Ct))!;
        await Task.Delay(TimeSpan.FromMilliseconds(1100), Ct);
        await using var taker = (await other.Store.Claim((await other.Store.Read(did, Ct))!, Ct))!;

        var result = await holder.Refreshed(Grant(did, "rotated"));
        var begun = await taker.Begin();
        await Task.Delay(TimeSpan.FromMilliseconds(600), Ct);
        await taker.DisposeAsync();

        Assert.NotNull(result);
        Assert.False(begun, "The taker reads the row again and finds its claim cleared.");
        var row = (await stopped.Store.Read(did, Ct))!;
        Assert.Equal("rotated", row.Grant!.TokenSet.RefreshToken);
        Assert.Null(row.Row.ClaimId);
    }

    [Fact]
    public async Task A_stale_result_after_a_new_sign_in_is_refused_and_the_sign_in_stands()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        await using var holder = (await node.Store.Claim((await node.Store.Read(did, Ct))!, Ct))!;
        Assert.NotNull(await node.Store.Commit(did, Grant(did, "signin"), await node.Store.Read(did, Ct), Ct));

        var result = await holder.Refreshed(Grant(did, "stale"));

        Assert.Null(result);
        var row = (await node.Store.Read(did, Ct))!;
        Assert.Equal("signin", row.Grant!.TokenSet.RefreshToken);
        Assert.Null(row.Row.ClaimId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("duckdb")]
    public async Task Two_holders_refreshing_from_one_grant_store_one_result(string adapter)
    {
        await using var world = await World(adapter);
        var did = world.Dids[0];
        var stopped = world.Node(claimLifetime: TimeSpan.FromSeconds(1), claimRenewal: TimeSpan.FromHours(1));
        var other = Process(world);
        await world.SignIn(stopped, did);
        await using var first = (await stopped.Store.Claim((await stopped.Store.Read(did, Ct))!, Ct))!;
        await Task.Delay(TimeSpan.FromMilliseconds(1100), Ct);
        await using var second = (await other.Store.Claim((await other.Store.Read(did, Ct))!, Ct))!;

        var secondResult = await second.Refreshed(Grant(did, "second"));
        var firstResult = await first.Refreshed(Grant(did, "first"));

        Assert.NotNull(secondResult);
        Assert.Null(firstResult);
        var row = (await stopped.Store.Read(did, Ct))!;
        Assert.Equal("second", row.Grant!.TokenSet.RefreshToken);
        Assert.Null(row.Row.ClaimId);
    }

    [Fact]
    public async Task A_result_that_landed_with_its_answer_lost_is_reported_stored_and_no_renewal_undoes_it()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        await world.SignIn(node, did);
        await using var claim = (await node.Store.Claim((await node.Store.Read(did, Ct))!, Ct))!;
        StoreFaults.ClaimedGrant = claim.Held.Row.Grant;
        Volatile.Write(ref StoreFaults.FailAfterResults, 1);

        var result = await claim.Refreshed(Grant(did, "rotated"));
        await Task.Delay(Renewal * 3, Ct);

        Assert.NotNull(result);
        var row = (await node.Store.Read(did, Ct))!;
        Assert.Equal("rotated", row.Grant!.TokenSet.RefreshToken);
        Assert.Null(row.Row.ClaimId);
    }

    [Fact]
    public async Task A_second_process_reading_while_a_result_stalls_within_its_window_finds_the_row_claimed_and_the_result_lands()
    {
        await using var world = await World();
        var did = world.Dids[0];
        var node = Process(world);
        var other = Process(world);
        await world.SignIn(node, did);
        await using var claim = (await node.Store.Claim((await node.Store.Read(did, Ct))!, Ct))!;
        StoreFaults.ClaimedGrant = claim.Held.Row.Grant;
        var taken = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        // The result write stalls 1.8 s of its 2 s window; the second process reads 300 ms after the first lease ends.
        Volatile.Write(ref StoreFaults.StallResultsMs, 1800);

        var result = claim.Refreshed(Grant(did, "stalled"));
        await Task.Delay(taken + Lifetime + TimeSpan.FromMilliseconds(300) - DateTimeOffset.UtcNow, Ct);
        var read = (await other.Store.Read(did, Ct))!;
        var stored = await result;

        Assert.True(read.Claimed, "Renewals went on while the result stalled.");
        Assert.NotNull(stored);
        Assert.Equal("stalled", (await node.Store.Read(did, Ct))!.Grant!.TokenSet.RefreshToken);
    }

    private static OAuthSessionData Grant(string did, string refresh)
    {
        using var key = DPoPKeyPair.Generate();
        return new OAuthSessionData
        {
            ClientId = SessionWorld.ClientId, DPoPKey = key.ExportKeyPair(),
            TokenSet = new TokenSet { Sub = did, Scope = "atproto", AccessToken = "a-" + refresh, RefreshToken = refresh, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
        };
    }
}
