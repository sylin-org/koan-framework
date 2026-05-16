using System;

namespace Koan.Jobs.Archival;

/// <summary>
/// Configures enterprise archival behavior for completed and failed jobs.
/// </summary>
public sealed class JobArchivalPolicy
{
    /// <summary>
    /// How long to retain completed/failed jobs before archival sweep removes them.
    /// Default: 30 days.
    /// </summary>
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Maximum number of jobs to delete per sweep cycle.
    /// Default: 500.
    /// </summary>
    public int BatchSize { get; init; } = 500;

    /// <summary>
    /// Interval between archival sweep cycles.
    /// Default: 6 hours.
    /// </summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Whether the archival service is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
