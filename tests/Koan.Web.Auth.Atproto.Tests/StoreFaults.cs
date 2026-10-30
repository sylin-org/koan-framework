using Koan.Web.Auth.Connector.Atproto.Storage;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// Store failures a spec arms, declared on <see cref="AtprotoSession"/>'s lifecycle so they act where a store would:
/// before a write lands, after it landed, or on a read. A claim is a write that gives the row a claim identity; a renewal
/// is one that keeps it; a result is one that clears it and stores another grant; a release clears it and keeps the
/// claimed grant.
/// </summary>
internal static class StoreFaults
{
    /// <summary>Every session write takes this long, honouring cancellation.</summary>
    public static int WriteDelayMs;

    /// <summary>A claim's write takes this long, honouring cancellation.</summary>
    public static int ClaimDelayMs;

    /// <summary>The next renewal write takes this long, honouring cancellation; whether it then reached the store, and landed.</summary>
    public static int SlowRenewalMs;
    public static volatile bool SlowRenewalReached;
    public static volatile bool SlowRenewalLanded;

    /// <summary>When the first renewal landed, in UTC ticks.</summary>
    public static long FirstRenewalLandedTicks;

    /// <summary>
    /// Result and release writes take this long in a store that ignores cancellation: a caller that gave up meanwhile still
    /// has the write made, under the guard it sent, and its call answers only then.
    /// </summary>
    public static int IgnoreCancellationMs;

    /// <summary>Claims and results as they land, in order: "claim" and its identity, or "result".</summary>
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Landings = new();

    /// <summary>Result writes that fail before they land, counted down.</summary>
    public static int FailResults;

    /// <summary>Result writes that land and then fail, counted down.</summary>
    public static int FailAfterResults;

    /// <summary>A result write lands, then fails after 300 ms while reads are slow for a second; every later result write fails.</summary>
    public static int ResultLandsThenFails;

    /// <summary>Once a result has landed, every later result write fails before it lands.</summary>
    public static volatile bool FailResultsOnceLanded;

    /// <summary>How long a result write stalls before it is written, or until when, honouring cancellation.</summary>
    public static int StallResultsMs;
    public static long StallResultsUntilTicks;

    /// <summary>The first result lands, then the store is down for this long — every write and read fails — and the write fails.</summary>
    public static int DownAfterLandingMs;

    /// <summary>Every write and read fails.</summary>
    public static volatile bool StoreDown;

    /// <summary>Reads fail, or hang until their bound.</summary>
    public static volatile bool ReadsFail;
    public static volatile bool ReadsHang;

    /// <summary>Reads fail until the claim's release is written.</summary>
    public static volatile bool FailReadsUntilRelease;

    /// <summary>Renewals fail before they land, or hang until their bound.</summary>
    public static volatile bool FailRenewals;
    public static volatile bool StallRenewals;

    /// <summary>One renewal lands, then fails 800 ms later; with <see cref="SlowReadsAfterRenewal"/>, reads are slow meanwhile.</summary>
    public static int LandThenFailRenewal;
    public static volatile bool SlowReadsAfterRenewal;

    /// <summary>The claimed grant: a write of any other grant that clears the claim is a result, and one of this grant a release.</summary>
    public static volatile string? ClaimedGrant;

    private static volatile bool slowReads;
    private static volatile string? slowRenewal;
    private static readonly AsyncLocal<bool> madeByTheStore = new();
    private static volatile bool resultLanded;

    public static void Reset()
    {
        FailResults = 0;
        FailAfterResults = 0;
        ResultLandsThenFails = 0;
        FailResultsOnceLanded = false;
        StallResultsMs = 0;
        StallResultsUntilTicks = 0;
        WriteDelayMs = 0;
        ClaimDelayMs = 0;
        SlowRenewalMs = 0;
        SlowRenewalReached = false;
        SlowRenewalLanded = false;
        FirstRenewalLandedTicks = 0;
        IgnoreCancellationMs = 0;
        Landings.Clear();
        slowRenewal = null;
        DownAfterLandingMs = 0;
        StoreDown = false;
        ReadsFail = false;
        ReadsHang = false;
        FailReadsUntilRelease = false;
        FailRenewals = false;
        StallRenewals = false;
        LandThenFailRenewal = 0;
        SlowReadsAfterRenewal = false;
        ClaimedGrant = null;
        slowReads = false;
        resultLanded = false;
    }

    public static void Declare()
    {
        AtprotoSession.Lifecycle.BeforeUpsert(async context =>
        {
            if (StoreDown) throw new InvalidOperationException("fixture: the store is unreachable");
            if (madeByTheStore.Value) return context.Proceed();
            if (Volatile.Read(ref WriteDelayMs) is > 0 and var slow) await Task.Delay(slow, context.CancellationToken);
            if (Claim(context.Prior, context.Current) && Volatile.Read(ref ClaimDelayMs) is > 0 and var claiming)
                await Task.Delay(claiming, context.CancellationToken);
            if (Release(context.Current)) FailReadsUntilRelease = false;
            if (Renewal(context.Prior, context.Current))
            {
                if (FailRenewals) throw new InvalidOperationException("fixture: the store failed this renewal");
                if (StallRenewals) await Task.Delay(Timeout.Infinite, context.CancellationToken);
                if (Interlocked.Exchange(ref SlowRenewalMs, 0) is > 0 and var renewing)
                {
                    slowRenewal = context.Current.Version;
                    await Task.Delay(renewing, context.CancellationToken);
                    SlowRenewalReached = true;
                }
            }

            var result = Result(context.Prior, context.Current);
            if ((result || Release(context.Current)) && context.Prior?.ClaimId is { } claimId
                && Volatile.Read(ref IgnoreCancellationMs) is > 0 and var ignoring)
            {
                await Task.Delay(ignoring);
                if (context.CancellationToken.IsCancellationRequested)
                {
                    // The caller gave up; the store writes anyway, under the guard the caller sent — the grant refreshed
                    // from for a result, the claim for a release — and answers then.
                    madeByTheStore.Value = true;
                    var from = ClaimedGrant;
                    _ = result
                        ? await AtprotoSession.ReplaceIf(context.Current, stored => stored.Grant == from, ct: CancellationToken.None)
                        : await AtprotoSession.ReplaceIf(context.Current, stored => stored.ClaimId == claimId, ct: CancellationToken.None);
                    context.CancellationToken.ThrowIfCancellationRequested();
                }
            }

            if (Result(context.Prior, context.Current))
            {
                if (new DateTimeOffset(Interlocked.Read(ref StallResultsUntilTicks), TimeSpan.Zero) - DateTimeOffset.UtcNow is { } left && left > TimeSpan.Zero)
                    await Task.Delay(left, context.CancellationToken);
                if (Volatile.Read(ref StallResultsMs) is > 0 and var stall) await Task.Delay(stall, context.CancellationToken);
                if (FailResultsOnceLanded && resultLanded) throw new InvalidOperationException("fixture: the store failed this write");
                if (Volatile.Read(ref ResultLandsThenFails) >= 2 || Interlocked.Decrement(ref FailResults) >= 0)
                    throw new InvalidOperationException("fixture: the store failed this write");
            }

            return context.Proceed();
        });
        AtprotoSession.Lifecycle.AfterUpsert(async context =>
        {
            if (Claim(context.Prior, context.Current)) Landings.Enqueue("claim " + context.Current.ClaimId);
            if (Result(context.Prior, context.Current))
            {
                resultLanded = true;
                Landings.Enqueue("result");
                if (Interlocked.Decrement(ref FailAfterResults) >= 0)
                    throw new InvalidOperationException("fixture: the store failed after this write landed");
                if (Interlocked.Exchange(ref DownAfterLandingMs, 0) is > 0 and var down)
                {
                    StoreDown = true;
                    _ = Task.Run(async () => { await Task.Delay(down); StoreDown = false; });
                    throw new InvalidOperationException("fixture: the result landed, then the store went down");
                }

                if (Interlocked.CompareExchange(ref ResultLandsThenFails, 2, 1) == 1)
                {
                    SlowFor(TimeSpan.FromSeconds(1));
                    await Task.Delay(300);
                    throw new InvalidOperationException("fixture: the store failed after this write landed");
                }
            }

            if (Renewal(context.Prior, context.Current))
            {
                Interlocked.CompareExchange(ref FirstRenewalLandedTicks, DateTimeOffset.UtcNow.UtcTicks, 0);
                if (context.Current.Version == slowRenewal) SlowRenewalLanded = true;
            }

            if (Renewal(context.Prior, context.Current) && Interlocked.Exchange(ref LandThenFailRenewal, 0) == 1)
            {
                if (SlowReadsAfterRenewal) SlowFor(TimeSpan.FromSeconds(1.2));
                await Task.Delay(800);
                throw new InvalidOperationException("fixture: the store failed after this renewal landed");
            }
        });
        AtprotoSession.Lifecycle.AfterLoad(async context =>
        {
            if (StoreDown || ReadsFail || FailReadsUntilRelease) throw new InvalidOperationException("fixture: the store failed this read");
            if (ReadsHang) await Task.Delay(Timeout.Infinite, context.CancellationToken);
            if (slowReads) await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
        });
    }

    public static bool Claim(AtprotoSession? prior, AtprotoSession current)
        => current.ClaimId is not null && current.ClaimId != prior?.ClaimId;

    public static bool Renewal(AtprotoSession? prior, AtprotoSession current)
        => prior?.ClaimId is not null && current.ClaimId == prior.ClaimId && current.ClaimedUntil is not null;

    public static bool Result(AtprotoSession? prior, AtprotoSession current)
        => current.ClaimId is null && current.ClaimedUntil is null && current.Grant is not null
           && (ClaimedGrant is { } claimed ? current.Grant != claimed : prior?.ClaimId is not null && current.Grant != prior.Grant);

    private static bool Release(AtprotoSession current)
        => current.ClaimId is null && current.ClaimedUntil is null && ClaimedGrant is { } claimed && current.Grant == claimed;

    /// <summary>Result writes stall until <paramref name="span"/> from now.</summary>
    public static void StallResultsFor(TimeSpan span) => Interlocked.Exchange(ref StallResultsUntilTicks, (DateTimeOffset.UtcNow + span).UtcTicks);

    private static void SlowFor(TimeSpan span)
    {
        slowReads = true;
        _ = Task.Run(async () => { await Task.Delay(span); slowReads = false; });
    }
}
