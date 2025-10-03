
using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Milvus;

public sealed class MilvusOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

    public string Endpoint { get; set; } = "http://localhost:19530";
    public string DatabaseName { get; set; } = "default";
    public string? CollectionName { get; set; } = null;
    public string PrimaryFieldName { get; set; } = "id";
    public string VectorFieldName { get; set; } = "embedding";
    public string MetadataFieldName { get; set; } = "metadata";
    public string Metric { get; set; } = "COSINE";
    public int DefaultTimeoutSeconds { get; set; } = 100;
    public int? Dimension { get; set; } = null;
    public bool AutoCreateCollection { get; set; } = true;
    public string ConsistencyLevel { get; set; } = "Bounded";
    public string? Token { get; set; } = null;
    public string? Username { get; set; } = null;
    public string? Password { get; set; } = null;

    // Query configuration for vector similarity search
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 200;

    // IAdapterOptions implementation - map to vector-specific properties
    public int DefaultPageSize
    {
        get => DefaultTopK;
        set => DefaultTopK = value;
    }
    public int MaxPageSize
    {
        get => MaxTopK;
        set => MaxTopK = value;
    }

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}

