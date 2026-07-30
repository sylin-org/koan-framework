using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Weaviate;

/// <summary>Weaviate placement, authentication, and bounded-work options.</summary>
public sealed class WeaviateOptions : IAdapterOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;
    public string Endpoint { get; set; } = Infrastructure.Constants.Defaults.Endpoint;
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.TimeoutSeconds;
    public int VisibilityTimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.VisibilityTimeoutSeconds;
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;
    public int MaxBatchPoints { get; set; } = Infrastructure.Constants.Defaults.MaxBatchPoints;
    public int MaxClearPoints { get; set; } = Infrastructure.Constants.Defaults.MaxClearPoints;
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;
    public int MaxResponseBytes { get; set; } = Infrastructure.Constants.Defaults.MaxResponseBytes;
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
