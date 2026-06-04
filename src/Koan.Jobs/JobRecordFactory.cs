namespace Koan.Jobs;

/// <summary>Builds a fresh <see cref="JobRecord"/> for a (work-item × action) — used by submit and by chain-advance,
/// so both compute lane / coalesce key / gate key / deadline identically.</summary>
internal static class JobRecordFactory
{
    /// <summary>A scheduled (level-triggered) action's jobs are <em>parked</em> until their reconcile sweep releases
    /// them, hence <see cref="DateTimeOffset.MaxValue"/>; edge-triggered jobs are visible now (or after a delay).</summary>
    public static DateTimeOffset VisibleAt(ResolvedActionPolicy policy, DateTimeOffset now, TimeSpan? after)
        => policy.Schedule is not null ? DateTimeOffset.MaxValue : after is { } d ? now + d : now;

    public static JobRecord Create(
        JobTypeBinding binding, ResolvedActionPolicy policy, object workItem,
        string workId, string action, DateTimeOffset now, TimeSpan? after, string? correlationId)
    {
        var visibleAt = VisibleAt(policy, now, after);
        var rec = new JobRecord
        {
            WorkType = binding.WorkType,
            WorkId = workId,
            Action = action,
            Status = JobStatus.Queued,
            VisibleAt = visibleAt,
            FirstSubmittedAt = now,
            Lane = policy.Lane,
            CoalesceKey = binding.CoalesceKey(workItem, action),
            GateKey = binding.GateKey(workItem),
            Deadline = now + policy.Deadline,
            CorrelationId = correlationId,
        };
        rec.Transitions.Add(new JobTransition { At = now, From = JobStatus.Created, To = JobStatus.Queued, Note = "submitted" });
        return rec;
    }
}
