namespace Koan.Jobs;

/// <summary>
/// Outcome of a single-job execution via <see cref="JobOrchestrator.ExecuteNextAsync"/>. Carries the control
/// signal the handler raised and the settle-time values so a test can assert them without a ledger query.
/// </summary>
public sealed record JobRunResult(
    string JobId,
    string WorkType,
    string Action,
    JobSignal Signal,
    DateTimeOffset? DeferUntil,
    string? NextAction,
    string? GateKeyOverride);
