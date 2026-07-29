using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Connector.ElasticSearch;

/// <summary>Elasticsearch placement, authentication, and bounded-work options.</summary>
public sealed class ElasticSearchOptions : IAdapterOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    public string Endpoint { get; set; } = Infrastructure.Constants.Defaults.Endpoint;
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.TimeoutSeconds;
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;
    public int MaxBatchPoints { get; set; } = Infrastructure.Constants.Defaults.MaxBatchPoints;
    public int MaxRequestBytes { get; set; } = Infrastructure.Constants.Defaults.MaxRequestBytes;
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;
    public int MaxResponseBytes { get; set; } = Infrastructure.Constants.Defaults.MaxResponseBytes;
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
