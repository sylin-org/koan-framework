namespace Koan.Jobs;

/// <summary>One appended row of the ledger's audit trail: a single status transition with a timestamp and note.</summary>
public sealed class JobTransition
{
    public DateTimeOffset At { get; set; }
    public JobStatus From { get; set; }
    public JobStatus To { get; set; }
    public string? Note { get; set; }
}
