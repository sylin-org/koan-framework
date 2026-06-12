namespace Koan.Jobs;

/// <summary>A translatable filter for the static facade (<c>MyModel.Jobs.Where(...)</c>) and dashboards.
/// Null fields are wildcards. Kept declarative (not an arbitrary predicate) so durable ledgers can push it down.</summary>
public sealed record JobQuery(
    string? WorkType = null,
    string? WorkId = null,
    string? Action = null,
    JobStatus? Status = null);
