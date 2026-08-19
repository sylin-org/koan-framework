using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.PgVector;

/// <summary>PostgreSQL placement and bounded-work options for the pgvector adapter.</summary>
public sealed class PgVectorOptions : IAdapterOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    /// <summary>
    /// PostgreSQL placement, or <c>auto</c> to use the selected PostgreSQL record source or service discovery.
    /// </summary>
    public string ConnectionString { get; set; } = Infrastructure.Constants.Configuration.Automatic;

    /// <summary>Maximum duration of one PostgreSQL command.</summary>
    public int CommandTimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.CommandTimeoutSeconds;

    /// <summary>Maximum UTF-8 bytes accepted for one neutral metadata object.</summary>
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;

    /// <summary>Maximum points accepted by one adapter batch operation.</summary>
    public int MaxBatchPoints { get; set; } = Infrastructure.Constants.Defaults.MaxBatchPoints;

    /// <summary>Maximum rows an exact vector query may return.</summary>
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
