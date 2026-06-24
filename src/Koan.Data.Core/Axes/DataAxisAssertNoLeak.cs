using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Axes;

public static partial class DataAxis
{
    /// <summary>
    /// The one-assertion cross-axis <b>isolation proof</b> (ARCH-0101 §10) — the generalization of the flagship
    /// <c>AssertNoTenantLeak</c> to <i>any</i> value-isolation axis. Through the <b>already-booted</b> ambient host
    /// (ARCH-0079) it exercises two contexts of the axis across the FULL matrix the discriminator must cover — <b>read</b>
    /// isolation · the <b>get-by-id IDOR</b> defence · the <b>[Cacheable] cache-key</b> partition · the <b>cross-scope
    /// write-takeover</b> (IDOR-on-write — the conflict-aware upsert must reject A clobbering B's row by id) · the
    /// <b>async-hop</b> carrier round-trip · scoped <b>DeleteMany / DeleteAll / RemoveAll</b> that never cross the
    /// boundary — and <b>throws</b> <see cref="DataAxisLeakDetectedException"/> (naming the failed check) on the first
    /// leak. A clean axis returns.
    ///
    /// <para>The axis is supplied as a scope-enter function (<paramref name="withContext"/>) — e.g. <c>Tenant.Use</c>,
    /// or a moderation/region scope verb — so the SAME proof rides any axis (the conformity-by-design promise: a future
    /// Moderation axis is proven by the identical call). Legs that the axis does not equip are skipped: the write-takeover
    /// runs only for a managed-field axis (the guarded upsert), the cache leg only for a <c>[Cacheable]</c> entity, the
    /// async-hop only when the axis registers an <c>IAmbientSliceCarrier</c>. The harness writes a handful of throwaway
    /// rows in a fresh isolation <see cref="EntityContext.Partition"/> and deletes them; it reads the live registries
    /// (the axis is already registered by the host's boot), it registers nothing.</para>
    /// </summary>
    /// <param name="withContext">Enters the axis's isolation scope for a context value (its disposal restores the prior ambient).</param>
    /// <param name="contextA">The first context value (a distinct isolation scope).</param>
    /// <param name="contextB">The second context value — must be mutually invisible to <paramref name="contextA"/>.</param>
    /// <param name="ct">A cancellation token threaded through the data operations.</param>
    /// <exception cref="DataAxisLeakDetectedException">A check observed a cross-context leak.</exception>
    public static async Task AssertNoLeak<TEntity, TKey>(
        Func<string, IDisposable> withContext,
        string contextA = "axis-a",
        string contextB = "axis-b",
        CancellationToken ct = default)
        where TEntity : Entity<TEntity, TKey>, new()
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(withContext);
        if (string.Equals(contextA, contextB, StringComparison.Ordinal))
            throw new ArgumentException("AssertNoLeak needs two DISTINCT context values to prove isolation.", nameof(contextB));

        // A fresh storage partition isolates this proof's rows from any other data sharing the store; the axis under
        // test is orthogonal and does the in-proof isolation.
        using var _partition = EntityContext.Partition("anl-" + Guid.CreateVersion7().ToString("n"));

        TKey a1, a2, b1;
        using (withContext(contextA))
        {
            a1 = (await Data<TEntity, TKey>.Upsert(new TEntity(), ct)).Id;
            a2 = (await Data<TEntity, TKey>.Upsert(new TEntity(), ct)).Id;
        }
        using (withContext(contextB))
            b1 = (await Data<TEntity, TKey>.Upsert(new TEntity(), ct)).Id;

        try
        {
            // 1. READ isolation — each context sees only its own rows.
            using (withContext(contextA)) AssertSameIds<TEntity, TKey>("read", await Data<TEntity, TKey>.All(ct), a1, a2);
            using (withContext(contextB)) AssertSameIds<TEntity, TKey>("read", await Data<TEntity, TKey>.All(ct), b1);

            // 2. GET-BY-ID IDOR — a cross-context key read returns null (not-found), never the other context's row.
            using (withContext(contextA)) AssertNull<TEntity, TKey>("get-by-id IDOR", await Data<TEntity, TKey>.Get(b1, ct), b1);
            using (withContext(contextB)) AssertNull<TEntity, TKey>("get-by-id IDOR", await Data<TEntity, TKey>.Get(a1, ct), a1);

            // 3. CACHE-KEY partition (only when [Cacheable]) — context A's cached row is never served to context B.
            if (typeof(TEntity).GetCustomAttribute<CacheableAttribute>() is not null)
            {
                using (withContext(contextA)) await Data<TEntity, TKey>.Get(a1, ct);                 // populate the cache under A
                using (withContext(contextB)) AssertNull<TEntity, TKey>("cache-key", await Data<TEntity, TKey>.Get(a1, ct), a1);
            }

            // 4. CROSS-SCOPE WRITE-TAKEOVER (IDOR-on-write; only for a managed-field axis whose conflict-aware upsert is
            //    guarded) — context A must NOT take over context B's row by upserting its id; the guard rejects it and
            //    B's row survives untouched. The single most security-critical write-side isolation check.
            if (ManagedFieldRegistry.ForType(typeof(TEntity)).Count > 0)
            {
                using (withContext(contextA))
                {
                    var hijack = new TEntity { Id = b1 };
                    var tookOver = false;
                    try { await Data<TEntity, TKey>.Upsert(hijack, ct); tookOver = true; }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("cross-scope", StringComparison.OrdinalIgnoreCase))
                    {
                        // the conflict-aware upsert rejected the cross-scope takeover — correct.
                    }
                    if (tookOver)
                        throw new DataAxisLeakDetectedException("write-takeover", typeof(TEntity),
                            $"context '{contextA}' took over context '{contextB}'s row id '{b1}' by upsert — the conflict-aware write guard did not reject it.");
                }
                using (withContext(contextB)) AssertNotNull<TEntity, TKey>("write-takeover", await Data<TEntity, TKey>.Get(b1, ct), b1);
            }

            // 5. ASYNC-HOP carrier round-trip (only when the axis registers a carrier) — capture A's slice, restore it in
            //    a fresh ambient (the durable-transport rehydrate), and prove the read is still scoped to A. A restore
            //    failure (a broken carrier) surfaces as the same leak exception so the proof never passes silently.
            var carriers = AppHost.Current?.GetService<AmbientCarrierRegistry>();
            if (carriers is not null)
            {
                IReadOnlyDictionary<string, string>? bag;
                using (withContext(contextA)) bag = carriers.Capture();
                if (bag is { Count: > 0 })
                {
                    try
                    {
                        using (carriers.Restore(bag))   // re-establish A's axis from the captured bag, in the now-unscoped context
                            AssertSameIds<TEntity, TKey>("async-hop", await Data<TEntity, TKey>.All(ct), a1, a2);
                    }
                    catch (DataAxisLeakDetectedException) { throw; }
                    catch (Exception ex)
                    {
                        throw new DataAxisLeakDetectedException("async-hop", typeof(TEntity),
                            $"the carrier capture→restore round-trip for context '{contextA}' failed: {ex.Message}");
                    }
                }
            }

            // 6. SCOPED DeleteMany over a MIX of ids — deletes only A's; B's id is silently not owned (batch delete-IDOR).
            using (withContext(contextA))
            {
                var deleted = await Data<TEntity, TKey>.DeleteMany(new[] { a1, b1 }, ct);
                if (deleted != 1)
                    throw new DataAxisLeakDetectedException("scoped DeleteMany", typeof(TEntity),
                        $"a delete under '{contextA}' over a mix of its own + '{contextB}'s ids deleted {deleted} rows (expected 1 — its own only).");
            }
            using (withContext(contextA)) AssertSameIds<TEntity, TKey>("scoped DeleteMany", await Data<TEntity, TKey>.All(ct), a2);
            using (withContext(contextB)) AssertSameIds<TEntity, TKey>("scoped DeleteMany", await Data<TEntity, TKey>.All(ct), b1);

            // 7. SCOPED DeleteAll — wipes ONLY A's remaining rows; B untouched (never an unscoped Clear).
            using (withContext(contextA)) await Data<TEntity, TKey>.DeleteAll(ct);
            using (withContext(contextA)) AssertSameIds<TEntity, TKey>("scoped DeleteAll", await Data<TEntity, TKey>.All(ct));
            using (withContext(contextB)) AssertSameIds<TEntity, TKey>("scoped DeleteAll", await Data<TEntity, TKey>.All(ct), b1);

            // 8. SCOPED RemoveAll — a DISTINCT facade path from DeleteAll; re-create A's rows, then RemoveAll under A
            //    wipes only A's; B untouched.
            using (withContext(contextA)) { await Data<TEntity, TKey>.Upsert(new TEntity(), ct); await Data<TEntity, TKey>.Upsert(new TEntity(), ct); }
            using (withContext(contextA)) await Data<TEntity, TKey>.RemoveAll(RemoveStrategy.Safe, ct);
            using (withContext(contextA)) AssertSameIds<TEntity, TKey>("scoped RemoveAll", await Data<TEntity, TKey>.All(ct));
            using (withContext(contextB)) AssertSameIds<TEntity, TKey>("scoped RemoveAll", await Data<TEntity, TKey>.All(ct), b1);
        }
        finally
        {
            // Best-effort cleanup of this proof's throwaway rows (the fresh partition already isolates them). Use
            // CancellationToken.None — cleanup must run even when the caller's token is cancelled.
            try { using (withContext(contextA)) await Data<TEntity, TKey>.DeleteAll(CancellationToken.None); } catch { /* best effort */ }
            try { using (withContext(contextB)) await Data<TEntity, TKey>.DeleteAll(CancellationToken.None); } catch { /* best effort */ }
        }
    }

    private static void AssertSameIds<TEntity, TKey>(string check, IReadOnlyList<TEntity> rows, params TKey[] expected)
        where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        var actual = rows.Select(r => r.Id).OrderBy(k => k).ToArray();
        var want = expected.OrderBy(k => k).ToArray();
        if (!actual.SequenceEqual(want))
            throw new DataAxisLeakDetectedException(check, typeof(TEntity),
                $"the context saw ids [{string.Join(", ", actual)}] but should see exactly [{string.Join(", ", want)}].");
    }

    private static void AssertNull<TEntity, TKey>(string check, TEntity? row, TKey otherId)
        where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        if (row is not null)
            throw new DataAxisLeakDetectedException(check, typeof(TEntity),
                $"a cross-context read of id '{otherId}' returned the other context's row instead of null.");
    }

    private static void AssertNotNull<TEntity, TKey>(string check, TEntity? row, TKey id)
        where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        if (row is null)
            throw new DataAxisLeakDetectedException(check, typeof(TEntity),
                $"the owning context's row id '{id}' was lost (a cross-context op clobbered it).");
    }
}
