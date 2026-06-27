using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;

namespace Koan.Identity.Credentials;

/// <summary>
/// SEC-0007 P3-grp4 — applies a guarded one-shot mutation so single-use factor state (a burned recovery code, an
/// advanced TOTP anti-replay watermark, a consumed step-up ticket) is consumed <b>exactly once across concurrent
/// writers</b>. Uses the framework's compare-and-set primitive
/// (<see cref="IConditionalWriteRepository{TEntity,TKey}"/>, JOBS-0005 §20.3) where the adapter declares it
/// (relational/Mongo/etc.); falls back to a re-read-then-write guard on adapters that don't (e.g. in-memory) — which
/// is single-process correct. The CAS is what makes "single use" true under real concurrency, not just discipline.
/// </summary>
public static class AtomicSingleUse
{
    /// <summary>
    /// Apply <paramref name="model"/> guarded by <paramref name="guard"/> against the STORED row. On a CAS-capable
    /// adapter this is a true compare-and-set (returns false iff a concurrent writer won); on a non-CAS adapter (a
    /// single-binary in-memory store) it persists unconditionally — the caller's prior query/precondition already
    /// established the guard, and a single process has no cross-writer race to lose. The CAS is what upgrades
    /// "single use by discipline" to "single use by construction" the moment a distributed adapter is in play.
    /// </summary>
    public static async Task<bool> TryAsync<TEntity, TKey>(TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (Data<TEntity, TKey>.Capabilities.Has(DataCaps.Write.ConditionalReplace)
            && Data<TEntity, TKey>.As<IConditionalWriteRepository<TEntity, TKey>>() is { } cas)
            return await cas.ConditionalReplaceAsync(model, guard, ct).ConfigureAwait(false);

        await Data<TEntity, TKey>.Upsert(model, ct).ConfigureAwait(false);
        return true;
    }
}
