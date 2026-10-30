using CarpaNet.OAuth.Storage;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>
/// One process's claim on a session row, to refresh or revoke its grant: a lease under a claim identity of its own.
/// <list type="bullet">
/// <item>The identity fences the holder's renewals, its release and its end: each is conditional on the row still
/// carrying it, and every other write leaves the row without it, so a holder never writes over a row it no longer holds,
/// whatever order its own writes land in.</item>
/// <item>The result of a refresh is fenced on the grant it refreshed from instead, and clears whatever claim holds the row
/// in the same write. A result that lands late still lands over a process that took the lapsed claim over, and that
/// taker's renewals and release, fenced on its own identity, are refused from then on and never write the old grant
/// back. No result lands over a newer grant: a sign-in's, an end, or another holder's result.</item>
/// <item>No write is cut off on a fraction of an interval. An attempt runs until the store answers or its deadline: the
/// lease it serves for a renewal; that lease, or an interval when less is left, for the release; a lease from its start
/// for the result or the end. Past it, its outcome is unknown, and the fence settles whatever it lands late.</item>
/// <item>Renewals run on their own, due one <see cref="AtprotoSessionStore.ClaimRenewal"/> after the lease last landed
/// started. While the latest has not answered, another starts every <see cref="AtprotoSessionStore.AttemptSpacing"/>,
/// each only ever extending the lease. A refused renewal is read: superseded by a newer renewal of this claim, it is
/// passed over; the claim is lost only when the row no longer carries it. Renewals stop once the claim is settled, lost,
/// or its lease ended with none landed.</item>
/// <item>A provider request starts only with at least <see cref="AtprotoSessionStore.RequestRoom"/> left on the lease
/// (<see cref="Begin"/>), so the store records what the request returns.</item>
/// </list>
/// A holder that stops keeps others waiting no longer than the lease. Disposing a claim that is not settled releases it.
/// </summary>
internal sealed class SessionClaim : IAsyncDisposable
{
    private readonly AtprotoSessionStore store;
    private readonly string id;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly List<Task> renewals = [];
    private readonly Task renewing;
    private TaskCompletionSource extended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private StoredSession held;
    private readonly List<Task> results = [];
    private int disposed;
    private volatile bool settled;
    private volatile bool lost;

    internal SessionClaim(AtprotoSessionStore store, StoredSession held)
    {
        this.store = store;
        this.held = held;
        id = held.Row.ClaimId!;
        renewing = Task.Run(Renew);
    }

    /// <summary>The claimed row as last known to hold the claim; its protected grant does not change while the claim holds.</summary>
    public StoredSession Held => held;

    /// <summary>The claimed grant.</summary>
    public OAuthSessionData Grant => held.Grant!;

    // The end of the lease the last write known to have landed holds.
    private DateTimeOffset Lease => held.Row.ClaimedUntil!.Value;

    /// <summary>
    /// Whether a provider request may start under the claim: true once at least <see cref="AtprotoSessionStore.RequestRoom"/>
    /// is left on the lease and the row, read again, still carries the claim and the grant it was taken on, which is the
    /// grant the request presents. With less room, the holder waits up to one renewal interval for a renewal to give it;
    /// false when none does, when the row cannot be read while room is left, or when the claim is lost — a late result
    /// cleared it — and the caller releases the claim and reads the row again.
    /// </summary>
    public async Task<bool> Begin()
    {
        var until = DateTimeOffset.UtcNow + store.ClaimRenewal;
        while (true)
        {
            Task landing;
            lock (gate) landing = extended.Task;
            if (settled || lost) return false;
            var now = DateTimeOffset.UtcNow;
            if (Lease - now >= store.RequestRoom) break;
            if (now >= until) return false;
            await Task.WhenAny(landing, Task.Delay(until - now));
        }

        var (answered, row) = await store.Peek(held.Row.Id, Lease - store.RequestRoom);
        if (!answered) return false;
        if (row is null || row.ClaimId != id || row.Grant != held.Row.Grant)
        {
            lost = true;
            return false;
        }

        Extend(row);
        return true;
    }

    /// <summary>
    /// Stores the grant a refresh returned, while the row still holds the grant it refreshed from, and clears the claim
    /// in the same write, whichever holds the row then. It is the one write that must land, since the provider rotated the
    /// refresh token as it answered: it keeps one version across its attempts, which start a third of an interval apart,
    /// each running to the end of a window of one lease, while renewals go on. A refused attempt means the row holds
    /// another grant, possibly this very result: the row is read, and null means the caller reads it again. When no
    /// attempt is known to have landed by the window's end, the claim is released, within the lease it starts in or an
    /// interval when less is left, and the row read once every attempt has answered, or an interval on:
    /// <list type="bullet">
    /// <item>a row holding this result means it was stored;</item>
    /// <item>a row holding another grant means the caller reads it again (null);</item>
    /// <item>while an attempt is still pending in the store, or when the row cannot be read, whether it was stored is not
    /// known: logged at Warning, and a <see cref="TimeoutException"/> to try again;</item>
    /// <item>a row still holding the grant refreshed from, with nothing pending, means it was not stored: logged at Error,
    /// and the person signs in again at the next use.</item>
    /// </list>
    /// </summary>
    public async Task<StoredSession?> Refreshed(OAuthSessionData grant)
    {
        if (settled) return null;
        var from = held.Row.Grant!;
        var next = store.Granted(held.Row.Id, held.Row.Did, grant);
        var window = DateTimeOffset.UtcNow + store.ClaimLifetime;
        Exception? failure = null;
        while (DateTimeOffset.UtcNow < window)
        {
            var started = DateTimeOffset.UtcNow;
            var outcome = await store.WriteRefreshed(next, from, window, Pending);
            if (outcome.HasLanded)
            {
                Settle();
                return new StoredSession(next, grant);
            }

            if (outcome.IsRefused)
            {
                Settle();
                var (_, row) = await store.Peek(next.Id, DateTimeOffset.UtcNow + store.ClaimRenewal);
                return row?.Version == next.Version ? new StoredSession(next, grant) : null;
            }

            failure = outcome.Failure ?? failure;
            var pause = (started + store.AttemptSpacing < window ? started + store.AttemptSpacing : window) - DateTimeOffset.UtcNow;
            if (pause > TimeSpan.Zero) await Task.Delay(pause);
        }

        await store.Write(AtprotoSessionStore.Copy(held.Row), id, ReleaseDeadline());
        Settle();
        // Read once every attempt has answered, or an interval on: an attempt that lands meanwhile is seen.
        var settledAll = await Answered(DateTimeOffset.UtcNow + store.ClaimRenewal);
        var (answered, stored) = await store.Peek(next.Id, DateTimeOffset.UtcNow + store.ClaimRenewal);
        if (!answered) throw Unknown(next, failure);
        if (stored?.Version == next.Version) return new StoredSession(next, grant);
        if (stored?.Grant != from) return null;
        throw settledAll ? Unstored(next, failure) : Unknown(next, failure);
    }

    /// <summary>Releases the claim, keeping the grant as it was. Nothing is written once the claim is settled.</summary>
    public async Task Release()
    {
        if (settled) return;
        Settle();
        await store.Write(AtprotoSessionStore.Copy(held.Row), id, ReleaseDeadline());
    }

    // A release runs to the end of the lease it starts in, or for an interval when less is left: it is fenced, so it may
    // land after the lease.
    private DateTimeOffset ReleaseDeadline()
    {
        var least = DateTimeOffset.UtcNow + store.ClaimRenewal;
        return Lease > least ? Lease : least;
    }

    /// <summary>
    /// Ends the claimed session: the result of a revocation, or of a provider that ended the grant. One attempt, within a
    /// lease from its start. False when the claim no longer holds the row — it was signed in again, or taken over — so this
    /// claim ended nothing. An attempt whose outcome is unknown throws.
    /// </summary>
    public async Task<bool> End()
    {
        if (settled) return false;
        var outcome = await store.End(held.Row, id, DateTimeOffset.UtcNow + store.ClaimLifetime);
        if (!outcome.HasLanded && !outcome.IsRefused)
            throw new InvalidOperationException("The AT session could not be ended; try again.", outcome.Failure);
        Settle();
        return outcome.HasLanded;
    }

    // A renewal falls due one interval after the lease last landed started; while the latest attempt has not landed,
    // another starts every attempt spacing. A landing wakes the loop, so the next falls due from it.
    private async Task Renew()
    {
        var latest = Lease - store.ClaimLifetime;
        while (!settled && !lost)
        {
            Task landing;
            lock (gate) landing = extended.Task;
            var serves = Lease;
            var started = serves - store.ClaimLifetime;
            var due = latest > started ? latest + store.AttemptSpacing : started + store.ClaimRenewal;
            var now = DateTimeOffset.UtcNow;
            if (now < due)
            {
                await Task.WhenAny(landing, Task.Delay(due - now, stop.Token));
                continue;
            }

            if (now >= serves)
            {
                // The lease ended with no renewal landed: no attempt can serve it now.
                lost = true;
                return;
            }

            latest = now;
            lock (renewals)
            {
                renewals.RemoveAll(static task => task.IsCompleted);
                renewals.Add(RenewOnce(now, serves));
            }
        }
    }

    // One renewal attempt, within the lease it serves. Refused, the row is read: still under this claim, a newer renewal
    // superseded it; without this claim, the claim is lost.
    private async Task RenewOnce(DateTimeOffset started, DateTimeOffset serves)
    {
        var next = AtprotoSessionStore.Renewed(held.Row, id, started + store.ClaimLifetime);
        var outcome = await store.Renew(next, id, serves, stop.Token);
        if (outcome.HasLanded) Extend(next);
        else if (outcome.IsRefused)
        {
            var (answered, row) = await store.Peek(next.Id, serves, stop.Token);
            if (!answered) return;
            if (row is null || row.ClaimId != id) lost = true;
            else if (row.ClaimedUntil >= next.ClaimedUntil) Extend(row);
        }
    }

    private void Extend(AtprotoSession row)
    {
        TaskCompletionSource landed;
        lock (gate)
        {
            if (row.ClaimedUntil <= held.Row.ClaimedUntil) return;
            held = held with { Row = row };
            landed = extended;
            extended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        landed.SetResult();
    }

    private void Pending(Task write)
    {
        lock (results) results.Add(write);
    }

    // Whether every attempt at the result has answered by the deadline: a store that honours cancellation answers the
    // attempts given up at the window's end at once, and one that ignores it may still land them.
    private async Task<bool> Answered(DateTimeOffset deadline)
    {
        Task[] attempts;
        lock (results) attempts = [.. results];
        var all = Task.WhenAll(attempts);
        _ = all.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var left = deadline - DateTimeOffset.UtcNow;
        if (left > TimeSpan.Zero) await Task.WhenAny(all, Task.Delay(left));
        return all.IsCompleted;
    }

    private InvalidOperationException Unstored(AtprotoSession next, Exception? failure)
    {
        store.LogUnstored(next, failure);
        return new InvalidOperationException("The refreshed AT session could not be stored; the person signs in again at its next use.", failure);
    }

    private TimeoutException Unknown(AtprotoSession next, Exception? failure)
    {
        store.LogUnknown(next, failure);
        return new TimeoutException("Whether the refreshed AT session was stored is not known yet; try again.", failure);
    }

    private void Settle()
    {
        settled = true;
        stop.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        await Release();
        stop.Cancel();
        await renewing;
        Task[] attempts;
        lock (renewals) attempts = [.. renewals];
        await Task.WhenAll(attempts);
        stop.Dispose();
    }
}
