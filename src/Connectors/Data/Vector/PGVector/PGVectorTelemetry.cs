using System.Diagnostics;

namespace Koan.Data.Connector.PGVector;

/// <summary>
/// Telemetry support for PGVector operations.
/// Provides ActivitySource for distributed tracing.
/// </summary>
public static class PGVectorTelemetry
{
    private const string ActivitySourceName = "Koan.Data.PGVector";

    /// <summary>
    /// ActivitySource for PGVector operations.
    /// Activities: vector.ensureCreated, vector.upsert, vector.search, etc.
    /// </summary>
    public static readonly ActivitySource Activity = new(ActivitySourceName, "0.7.0");
}
