namespace Koan.Data.ElasticSearch;

public sealed class ElasticSearchOptions
{
    public string Endpoint { get; set; } = "http://localhost:9200";
    public string? IndexPrefix { get; set; }
        = "koan";
    public string? IndexName { get; set; }
        = null;
    public string VectorField { get; set; } = "embedding";
    public string MetadataField { get; set; } = "metadata";
    public string IdField { get; set; } = "id";
    public string SimilarityMetric { get; set; } = "cosine";
    public string RefreshMode { get; set; } = "wait_for";
    public int DefaultTimeoutSeconds { get; set; } = 100;
    public int? Dimension { get; set; }
        = null;
    public string? ApiKey { get; set; }
        = null;
    public string? Username { get; set; }
        = null;
    public string? Password { get; set; }
        = null;
    public bool DisableIndexAutoCreate { get; set; }
        = false;
}
