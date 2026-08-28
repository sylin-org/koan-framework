using Koan.Data.Abstractions.Analytics;

namespace Koan.Data.Analytics.Recipes;

/// <summary>
/// The materialization vocabulary inside <c>Materialize(...)</c>: declare the refresh cadence, the
/// freshness tolerance, and whether a stale read re-materializes on its way to answering.
/// </summary>
public sealed class AnalyticsMaterializationBuilder
{
    /// <summary>Scheduled refresh cadence. Omit for trigger-only refresh.</summary>
    public AnalyticsMaterializationBuilder Every(TimeSpan interval)
    {
        Policy = Policy with { Interval = interval };
        return this;
    }

    /// <summary>
    /// Declared freshness tolerance: an answer at most this old is served from the materialization;
    /// anything staler computes live (and backfills, if declared) — and says which it did.
    /// </summary>
    public AnalyticsMaterializationBuilder ServeWithin(TimeSpan tolerance)
    {
        Policy = Policy with { ServeWithin = tolerance };
        return this;
    }

    /// <summary>A stale read re-materializes on its way to answering (the self-healing posture).</summary>
    public AnalyticsMaterializationBuilder BackfillOnRead()
    {
        Policy = Policy with { BackfillOnRead = true };
        return this;
    }

    internal AnalyticsProjectionPolicy Policy { get; private set; } = new();
}
