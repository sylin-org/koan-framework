namespace Koan.Jobs;

/// <summary>
/// A cheap, bounded health snapshot of the whole ledger (JOBS-0008 self-reporting) — the signals whose absence let the
/// lane-starvation incident run undetected (a stalled queue presents as a healthy process). Deliberately GLOBAL, not
/// per-lane: the auto-probe (run at boot and on demand by the framework health prober) must be O(few index-served
/// queries), never O(lanes × backlog-scan) — a per-lane fan-out is a scan-storm on stores without a reliable per-lane
/// index and contends with real work. <see cref="OldestQueuedAge"/> IS the starvation tripwire: if the oldest due job
/// anywhere has waited past the budget, some lane is starved — enough to alert; the per-lane drill-down is a separate,
/// on-demand concern.
/// </summary>
/// <param name="Queued">Count of <c>Queued</c> jobs (any lane).</param>
/// <param name="Running">Count of <c>Running</c> jobs.</param>
/// <param name="ReclaimBacklog">Running jobs whose lease has lapsed (the reaper's pending input).</param>
/// <param name="OldestQueuedAge">How long the oldest <em>due</em> (<c>VisibleAt &lt;= now</c>) queued job has waited;
/// zero when nothing is due — the starvation tripwire.</param>
public sealed record JobsHealthSnapshot(long Queued, long Running, long ReclaimBacklog, TimeSpan OldestQueuedAge);
