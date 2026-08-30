using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Chroma;

/// <summary>Chroma placement, authentication, and bounded-work options.</summary>
public sealed class ChromaOptions : IAdapterOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    /// <summary>Chroma REST endpoint (server root; the adapter appends the v2 tenant/database path).</summary>
    public string Endpoint { get; set; } = Infrastructure.Constants.Defaults.Endpoint;

    /// <summary>Chroma tenant. The standalone server default is <c>default_tenant</c>.</summary>
    public string Tenant { get; set; } = Infrastructure.Constants.Defaults.Tenant;

    /// <summary>Chroma database inside the tenant. The standalone default is <c>default_database</c>.</summary>
    public string Database { get; set; } = Infrastructure.Constants.Defaults.Database;

    /// <summary>Optional bearer token sent as <c>Authorization: Bearer</c> (server auth configurations).</summary>
    public string? ApiKey { get; set; }

    /// <summary>HTTP timeout applied to one provider request.</summary>
    public int TimeoutSeconds { get; set; } = Infrastructure.Constants.Defaults.TimeoutSeconds;

    /// <summary>Maximum encoded neutral metadata accepted for one point.</summary>
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;

    /// <summary>Maximum points accepted by one adapter batch operation.</summary>
    public int MaxBatchPoints { get; set; } = Infrastructure.Constants.Defaults.MaxBatchPoints;

    /// <summary>Maximum candidates requested while resolving a stable search cutoff tie.</summary>
    public int MaxSearchCandidates { get; set; } = Infrastructure.Constants.Defaults.MaxSearchCandidates;

    /// <summary>Maximum buffered bytes accepted from one Chroma response.</summary>
    public int MaxResponseBytes { get; set; } = Infrastructure.Constants.Defaults.MaxResponseBytes;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
