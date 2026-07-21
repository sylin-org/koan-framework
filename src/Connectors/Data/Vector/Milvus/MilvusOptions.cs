
using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Milvus;

public sealed class MilvusOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

    public string Endpoint { get; set; } = Infrastructure.Constants.DefaultEndpoint;
    public string DatabaseName { get; set; } = "default";
    public string? CollectionName { get; set; } = null;
    public string PrimaryFieldName { get; set; } = "id";
    public string VectorFieldName { get; set; } = "embedding";
    public string MetadataFieldName { get; set; } = "metadata";
    public string Metric { get; set; } = "COSINE";
    public int DefaultTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Optional embedding dimension for explicit collection pre-creation. Ordinary writes derive it from the
    /// first embedding instead of guessing a model.
    /// </summary>
    public int? Dimension { get; set; }
    public bool AutoCreateCollection { get; set; } = true;
    public string ConsistencyLevel { get; set; } = "Bounded";
    public string? Token { get; set; } = null;
    public string? Username { get; set; } = null;
    public string? Password { get; set; } = null;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}

