
namespace Koan.Data.Milvus;

public sealed class MilvusOptions
{
    public string Endpoint { get; set; } = "http://localhost:19530";
    public string DatabaseName { get; set; } = "default";
    public string? CollectionName { get; set; }
        = null;
    public string PrimaryFieldName { get; set; } = "id";
    public string VectorFieldName { get; set; } = "embedding";
    public string MetadataFieldName { get; set; } = "metadata";
    public string Metric { get; set; } = "COSINE";
    public int DefaultTimeoutSeconds { get; set; } = 100;
    public int? Dimension { get; set; } = null;
    public bool AutoCreateCollection { get; set; } = true;
    public string ConsistencyLevel { get; set; } = "Bounded";
    public string? Token { get; set; }
        = null;
    public string? Username { get; set; }
        = null;
    public string? Password { get; set; }
        = null;
}
