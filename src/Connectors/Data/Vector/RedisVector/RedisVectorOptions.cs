using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.RedisVector;

/// <summary>Bounded-work options for Redis Search vector operations.</summary>
public sealed class RedisVectorOptions : IAdapterOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    /// <summary>Maximum UTF-8 bytes accepted for one neutral metadata object.</summary>
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;

    /// <summary>Maximum points accepted by one adapter batch operation.</summary>
    public int MaxBatchPoints { get; set; } = Infrastructure.Constants.Defaults.MaxBatchPoints;

    /// <summary>Maximum rows requested by one exact vector search.</summary>
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;

    /// <summary>Maximum distinct metadata paths projected from one point and admitted to dynamic native schema.</summary>
    public int MaxIndexedPaths { get; set; } = Infrastructure.Constants.Defaults.MaxIndexedPaths;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
