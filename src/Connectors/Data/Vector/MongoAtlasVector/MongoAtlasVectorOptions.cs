using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.MongoAtlasVector;

/// <summary>Atlas placement and bounded-work options for MongoAtlasVector.</summary>
public sealed class MongoAtlasVectorOptions : IAdapterOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    /// <summary>MongoDB endpoint, or <c>auto</c> to reuse the selected Mongo service.</summary>
    public string ConnectionString { get; set; } = Infrastructure.Constants.Configuration.Automatic;

    /// <summary>Vector-owned database. It deliberately does not inherit the record connector database.</summary>
    public string Database { get; set; } = Infrastructure.Constants.Defaults.Database;

    /// <summary>Maximum time used for Mongo connection and server selection.</summary>
    public int CommandTimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.CommandTimeoutSeconds;

    /// <summary>Maximum UTF-8 bytes accepted for one neutral metadata object.</summary>
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;

    /// <summary>Maximum points accepted by one adapter batch operation.</summary>
    public int MaxBatchPoints { get; set; } = Infrastructure.Constants.Defaults.MaxBatchPoints;

    /// <summary>Maximum rows requested by one exact vector search.</summary>
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;

    /// <summary>Maximum time to wait for an asynchronous Atlas Search index build.</summary>
    public int IndexReadyTimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.IndexReadyTimeoutSeconds;

    /// <summary>Maximum time an awaited mutation may take to become visible to Atlas Search.</summary>
    public int MutationVisibilityTimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.MutationVisibilityTimeoutSeconds;

    /// <summary>Delay between bounded Atlas Search visibility probes.</summary>
    public int VisibilityPollMilliseconds { get; set; } = Infrastructure.Constants.Defaults.VisibilityPollMilliseconds;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
