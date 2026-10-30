namespace Koan.Jobs;

/// <summary>
/// A running execution's own row (JOBS-0010): the copy it last wrote, and the one way it writes. Its lease renewals, its
/// progress and its settlement take turns, each a compare on the revision last written, so a live claim pays one write and
/// no read. A miss means another writer changed the row. The row is read again: while it still runs under this claim
/// — this owner at this attempt, which a reclaim and the next claim always change — the change is applied onto the row
/// as stored, so a cancellation mark stands; otherwise the claim is gone and nothing is written.
/// </summary>
internal sealed class ClaimedRow(IJobLedger ledger, JobRecord claimed)
{
    /// <summary>Writes one change makes against rows other writers keep changing. Few writers touch a running row, so a
    /// claim that keeps missing is left to lapse with its lease, and the reaper decides.</summary>
    private const int WriteAttempts = 3;

    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly string? _owner = claimed.Owner;
    private readonly int _attempt = claimed.Attempt;
    private JobRecord _row = claimed;

    /// <summary>Apply <paramref name="change"/> to this claim's row. Returns the row as written, or null when the claim is
    /// gone or has already ended. A store failure surfaces to the caller: the row is unchanged, or the next read tells.
    /// </summary>
    public async Task<JobRecord?> Write(Action<JobRecord> change, CancellationToken ct)
    {
        await _turn.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= WriteAttempts && _row.Status == JobStatus.Running; attempt++)
            {
                var next = _row.Clone();
                change(next);
                if (await ledger.Update(next, ct)) return _row = next;

                var stored = await ledger.Get(_row.Id, ct);
                if (stored is not { Status: JobStatus.Running } || stored.Owner != _owner || stored.Attempt != _attempt) return null;
                _row = stored;
            }
            return null;
        }
        finally
        {
            _turn.Release();
        }
    }
}
