using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core;
using Koan.Identity.Infrastructure;

namespace Koan.Identity.Audit;

/// <summary>
/// SEC-0007 Layer 3 (optional) — tamper-evident audit. When enabled (<c>Koan:Identity:HashChainAudit</c>), each
/// <see cref="AuditEvent"/> is stamped with a monotonic <c>Sequence</c> and a <c>Hash</c> =
/// SHA-256(sequence | prevHash | canonical content), chained from the prior event. Editing or removing any past
/// event breaks the recomputation, which <see cref="VerifyAsync"/> detects and pinpoints. Opt-in because chaining
/// serializes audit writes through the chain head — a deliberate cost for a tamper-evident trail. (Cross-node strict
/// ordering needs a durable coordinated head — a follow-on; the in-process head detects all single-store tampering.)
/// </summary>
public sealed class AuditChain
{
    private const string Genesis = "GENESIS";
    private const char Sep = (char)0x1f;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _lastSequence = -1;
    private string _lastHash = Genesis;
    private volatile bool _seeded;

    /// <summary>
    /// Stamp Sequence/PrevHash/Hash and persist atomically: the chain head advances ONLY after the write succeeds,
    /// so a swallowed/failed audit write never leaves a permanent gap (which would make VerifyAsync report a false
    /// tamper forever). Serialized through the chain head — the documented cost of opting into chaining.
    /// </summary>
    public async Task AppendAsync(AuditEvent e, Func<AuditEvent, Task> save, CancellationToken ct = default)
    {
        if (!_seeded) await SeedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Fix the timestamp now so the hashed value equals the stored one ([Timestamp] then leaves it).
            if (e.OccurredAt == default) e.OccurredAt = DateTimeOffset.UtcNow;
            e.Sequence = _lastSequence + 1;
            e.PrevHash = _lastHash;
            e.Hash = Compute(e.Sequence, e.PrevHash, e);

            await save(e).ConfigureAwait(false); // commit first…

            _lastSequence = e.Sequence;          // …then advance the head (so a failed write leaves it untouched)
            _lastHash = e.Hash;
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
    /// chain is re-hashed from the first changed event while the append lock is held. Used by privacy erasure; it
    /// refuses to bless unexplained prior tampering.
    /// </summary>
    internal async Task<(int Changed, int Rehashed)> RewriteAsync(
        Func<AuditEvent, bool> rewrite,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
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

            if (chained.Count > 0)
            {
                _lastSequence = chained[^1].Sequence;
                _lastHash = chained[^1].Hash!;
            }
            else
            {
                _lastSequence = -1;
                _lastHash = Genesis;
            }
            _seeded = true;
            return (changed, rehashed);
        }
        finally
        {
            _lock.Release();
        }
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

    private async Task SeedAsync(CancellationToken ct)
    {
        var chained = await LoadChainAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_seeded) return;
            if (chained.Count > 0)
            {
                _lastSequence = chained[^1].Sequence;
                _lastHash = chained[^1].Hash!;
            }
            _seeded = true;
        }
        finally
        {
            _lock.Release();
        }
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
