using Koan.Data.Analytics;

namespace Koan.Data.Connector.DuckDb.Runtime;

/// <summary>DuckDB's election as the analytics substrate (DATA-0123's reference engine).</summary>
internal sealed class DuckDbAnalyticsEngine : IAnalyticsEngine
{
    internal static readonly DuckDbAnalyticsEngine Instance = new();

    private DuckDbAnalyticsEngine() { }

    public string Name => "duckdb";
}
