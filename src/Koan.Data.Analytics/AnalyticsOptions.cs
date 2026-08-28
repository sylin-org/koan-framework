using Koan.Data.Analytics.Infrastructure;

namespace Koan.Data.Analytics;

public sealed class AnalyticsOptions
{
    /// <summary>Default cap on rows an on-demand answer may carry. Overridable per question.</summary>
    public int RowCap { get; set; } = Constants.DefaultRowCap;

    /// <summary>Wall-clock ceiling for one on-demand ask.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(Constants.DefaultTimeoutSeconds);

    /// <summary>
    /// The in-host scheduled refresh loop (boot catch-up + cadence ticking). Default on. Disable when
    /// an external scheduler drives freshness exclusively, or in tests where several hosts share one
    /// process and each must own its own refresh timing deterministically.
    /// </summary>
    public bool RefreshLoopEnabled { get; set; } = true;

    /// <summary>
    /// The HTTP refresh trigger door (<c>POST /analytics/{recipe}/refresh</c>). Disabled by default:
    /// an unauthenticated door that triggers aggregation scans is a load amplifier. Enable it when the
    /// route is gated and an external scheduler should drive freshness; the in-host loop, boot
    /// catch-up, backfill-on-read, and programmatic question.RefreshAsync work regardless.
    /// </summary>
    public bool AllowHttpRefreshTrigger { get; set; }

    /// <summary>
    /// Where materialized projections live: a per-host DuckDB file. Per-host is not a limitation but the
    /// posture — the engine is single-writer per file, and a derived store that rebuilds from the record
    /// store wants exactly that topology.
    /// </summary>
    public string MaterializationConnectionString { get; set; } =
        $"Data Source={Path.Combine(".koan", "analytics", "Koan.duckdb")}";
}
