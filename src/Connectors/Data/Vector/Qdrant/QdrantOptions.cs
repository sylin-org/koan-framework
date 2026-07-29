using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Qdrant;

/// <summary>Qdrant placement, authentication, and bounded-work options.</summary>
public sealed class QdrantOptions : IAdapterOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    /// <summary>Qdrant REST endpoint. The local default is used when discovery finds no service.</summary>
    public string Endpoint { get; set; } = Infrastructure.Constants.Defaults.Endpoint;

    /// <summary>Optional Qdrant API key sent through the provider's <c>api-key</c> header.</summary>
    public string? ApiKey { get; set; }

    /// <summary>HTTP timeout applied to one provider request.</summary>
    public int TimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.TimeoutSeconds;

    /// <summary>Maximum encoded neutral metadata accepted for one point.</summary>
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;

    /// <summary>Maximum points accepted by one adapter batch operation.</summary>
    public int MaxBatchPoints { get; set; } = Infrastructure.Constants.Defaults.MaxBatchPoints;

    /// <summary>Maximum candidates requested while resolving a stable search cutoff tie.</summary>
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;

    /// <summary>Maximum buffered bytes accepted from one Qdrant response.</summary>
    public int MaxResponseBytes { get; set; } = Infrastructure.Constants.Defaults.MaxResponseBytes;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
