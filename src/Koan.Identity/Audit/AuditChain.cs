using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Identity.Infrastructure;

namespace Koan.Identity.Audit;

/// <summary>
/// SEC-0007 Layer 3 (optional) — tamper-evident audit. When enabled (<c>Koan:Identity:HashChainAudit</c>), each
/// <see cref="AuditEvent"/> is stamped with a monotonic <c>Sequence</c> and a <c>Hash</c> =
/// SHA-256(sequence | prevHash | canonical content), chained from the prior event. Editing or removing any past
/// event breaks the recomputation, which <see cref="VerifyAsync"/> detects and pinpoints. Opt-in because chaining
/// serializes audit writes — a deliberate cost for a tamper-evident trail. The head is the top of the store, read in the
/// transaction the append writes in, so a rollback takes its numbers with it (ARCH-0142). On a route that holds, an append joins
/// the open hold or is a short hold of its own, the store's turn being the line, which holds across processes too; on
/// any other route a process-wide lock is the line, and a second process needs a coordinated head of its own.
/// </summary>
public sealed class AuditChain
{
    private const string Genesis = "GENESIS";
    private const char Sep = (char)0x1f;

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Stamp Sequence/PrevHash/Hash from the store's head and persist, as one turn. A write that fails or rolls back leaves no head to
    /// correct, so a verification never reports a gap nobody made. In a scope that defers its writes the append follows its commit,
    /// since nothing it would chain from has landed.
    /// </summary>
    public Task AppendAsync(AuditEvent e, Func<AuditEvent, Task> save, CancellationToken ct = default)
    {
        if (EntityContext.CurrentTransaction is { IsHeld: false, IsCompleted: false } deferring)
        {
            deferring.AfterCommit(token => AppendAsync(e, save, token));
            return Task.CompletedTask;
        }

        return InATurn(async () =>
        {
            var head = await Head(ct).ConfigureAwait(false);
            // Fix the timestamp now so the hashed value equals the stored one ([Timestamp] then leaves it).
            if (e.OccurredAt == default) e.OccurredAt = DateTimeOffset.UtcNow;
            e.Sequence = head is null ? 0 : head.Sequence + 1;
            e.PrevHash = head?.Hash ?? Genesis;
            e.Hash = Compute(e.Sequence, e.PrevHash, e);
            await save(e).ConfigureAwait(false);
            return true;
        }, ct);
    }

    /// <summary>The chain's last event: the top of its column, in one row, except while an unchained event, which holds zero, ties with the chain's first.</summary>
    private static async Task<AuditEvent?> Head(CancellationToken ct)
    {
        var top = await Data<AuditEvent, string>.Top(sort => sort.OrderByDescending(audit => audit.Sequence), ct).ConfigureAwait(false);
        return top is { Sequence: 0, Hash: null }
            ? (await AuditEvent.Query(audit => audit.Hash != null, ct).ConfigureAwait(false)).FirstOrDefault()
            : top;
    }

    /// <summary>One writer of the chain at a time: the hold it is in, a short hold of its own, or where a route cannot hold, the lock.</summary>
    private async Task<T> InATurn<T>(Func<Task<T>> work, CancellationToken ct)
    {
        // A scope that has ended holds nothing, though its block has yet to close.
        if (EntityContext.CurrentTransaction is { IsCompleted: true })
            using (EntityContext.With(preserveTransaction: false))
                return await InATurn(work, ct).ConfigureAwait(false);

        if (EntityContext.CurrentTransaction is { IsHeld: true }) return await work().ConfigureAwait(false);
        if (EntityContext.CurrentTransaction is null && Data<AuditEvent, string>.Capabilities.Has(DataCaps.Write.HeldTransaction))
        {
            using (await EntityContext.Hold("audit-chain", ct))
            {
                var result = await work().ConfigureAwait(false);
                await EntityContext.Commit(ct).ConfigureAwait(false);
                return result;
            }
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Recompute the chain from the store and report the first break (if any).</summary>
    public async Task<AuditChainVerification> VerifyAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var chained = await LoadChainAsync(ct).ConfigureAwait(false);
            return Verify(chained);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Authorize a bounded rewrite of chained audit content, but only when the pre-existing chain verifies. The
    /// chain is re-hashed from the first changed event in one turn. Used by privacy erasure; it
    /// refuses to bless unexplained prior tampering.
    /// </summary>
    internal Task<(int Changed, int Rehashed)> RewriteAsync(
        Func<AuditEvent, bool> rewrite,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        return InATurn(async () =>
        {
            var chained = await LoadChainAsync(ct).ConfigureAwait(false);
            var before = Verify(chained);
            if (!before.Intact)
                throw new InvalidOperationException(
                    $"Identity audit erasure refused: the existing hash chain is invalid at sequence " +
                    $"{before.EventsOrBrokenAt} ({before.Detail}). Investigate or restore the chain before retrying.");

            var previous = Genesis;
            var changed = 0;
            var rehashed = 0;
            var rewriteTail = false;
            foreach (var auditEvent in chained)
            {
                ct.ThrowIfCancellationRequested();
                if (rewrite(auditEvent))
                {
                    changed++;
                    rewriteTail = true;
                }

                if (rewriteTail)
                {
                    auditEvent.PrevHash = previous;
                    auditEvent.Hash = Compute(auditEvent.Sequence, previous, auditEvent);
                    await auditEvent.Save(ct).ConfigureAwait(false);
                    rehashed++;
                }

                previous = auditEvent.Hash!;
            }

            return (changed, rehashed);
        }, ct);
    }

    private static AuditChainVerification Verify(IReadOnlyList<AuditEvent> chained)
    {
        var prev = Genesis;
        long expected = 0;
        foreach (var e in chained)
        {
            if (e.Sequence != expected)
                return new AuditChainVerification(false, e.Sequence, $"sequence gap: expected {expected}, found {e.Sequence} (an event was removed or reordered)");
            if (e.PrevHash != prev)
                return new AuditChainVerification(false, e.Sequence, "prev-hash mismatch (an earlier event was altered or removed)");
            if (Compute(e.Sequence, prev, e) != e.Hash)
                return new AuditChainVerification(false, e.Sequence, "content hash mismatch (this event was altered after the fact)");
            prev = e.Hash!;
            expected++;
        }
        return new AuditChainVerification(true, chained.Count, "intact");
    }

    private static async Task<List<AuditEvent>> LoadChainAsync(CancellationToken ct)
    {
        var chained = new List<AuditEvent>();
        for (var page = 1; ; page++)
        {
            var batch = await AuditEvent.Page(
                page,
                IdentityErasureConstants.PageSize,
                sort => sort.OrderBy(audit => audit.Sequence).ThenBy(audit => audit.Id),
                ct).ConfigureAwait(false);
            chained.AddRange(batch.Where(static audit => audit.Hash is not null));
            if (batch.Count < IdentityErasureConstants.PageSize) break;
        }
        return chained.OrderBy(static audit => audit.Sequence).ToList();
    }

    private static string Compute(long sequence, string prevHash, AuditEvent e)
    {
        var canonical = string.Join(Sep,
            sequence.ToString(), prevHash, e.Actor ?? "", e.Subject ?? "", e.Action, e.Target ?? "",
            e.Before ?? "", e.After ?? "", e.Context ?? "", e.OccurredAt.UtcDateTime.ToString("O"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>The result of an audit-chain verification: intact, or the sequence + reason of the first break.</summary>
public sealed record AuditChainVerification(bool Intact, long EventsOrBrokenAt, string Detail);
