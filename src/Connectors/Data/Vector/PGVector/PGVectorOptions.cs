namespace Koan.Data.Connector.PGVector;

/// <summary>
/// Configuration options for PGVector adapter.
/// Controls connection, indexing strategy, and performance tuning.
/// </summary>
public sealed class PGVectorOptions
{
    /// <summary>
    /// PostgreSQL connection string.
    /// Example: "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass"
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Default embedding dimension for new tables.
    /// Common values: 384 (MiniLM), 768 (BERT), 1536 (OpenAI ada-002), 3072 (OpenAI large).
    /// </summary>
    public int DefaultDimension { get; set; } = 1536;

    /// <summary>
    /// Default number of results to return in searches.
    /// </summary>
    public int DefaultTopK { get; set; } = 10;

    /// <summary>
    /// Automatically create index on table creation.
    /// Recommended: true for production, false for bulk loading (create index after).
    /// </summary>
    public bool AutoCreateIndex { get; set; } = true;

    /// <summary>
    /// Default index type for automatic index creation.
    /// HNSW: Better recall, slower build, more memory.
    /// IVFFlat: Faster build, less memory, slightly lower recall.
    /// </summary>
    public IndexType DefaultIndexType { get; set; } = IndexType.Hnsw;

    /// <summary>
    /// HNSW index parameter: maximum number of connections per layer.
    /// Higher = better recall, more memory.
    /// Recommended: 16 (balanced), 32 (high quality), 8 (fast build).
    /// </summary>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW index parameter: size of dynamic candidate list during index construction.
    /// Higher = better index quality, slower build.
    /// Recommended: 64 (balanced), 128 (high quality), 32 (fast build).
    /// </summary>
    public int HnswEfConstruction { get; set; } = 64;

    /// <summary>
    /// IVFFlat index parameter: number of inverted lists (clusters).
    /// Rule of thumb: sqrt(rows).
    /// Recommended: 100 (small datasets), 1000 (medium), 10000 (large).
    /// </summary>
    public int IvfFlatLists { get; set; } = 100;

    /// <summary>
    /// Batch size for vector export operations.
    /// </summary>
    public int ExportBatchSize { get; set; } = 1000;

    /// <summary>
    /// Distance metric for vector similarity.
    /// Cosine: 1 - (embedding &lt;=&gt; query) [normalized vectors]
    /// L2: embedding &lt;-&gt; query [Euclidean distance]
    /// InnerProduct: embedding &lt;#&gt; query [dot product]
    /// </summary>
    public DistanceMetric DistanceMetric { get; set; } = DistanceMetric.Cosine;

    /// <summary>
    /// Maximum dimension supported.
    /// pgvector v0.6.0+ supports up to 16,000 dimensions.
    /// </summary>
    public const int MaxDimension = 16000;
}

/// <summary>
/// Supported index types for pgvector.
/// </summary>
public enum IndexType
{
    /// <summary>
    /// No index (sequential scan). Use for very small datasets or during bulk loading.
    /// </summary>
    None = 0,

    /// <summary>
    /// Hierarchical Navigable Small World (HNSW) index.
    /// Pros: Excellent recall, fast queries.
    /// Cons: Slower index build, higher memory usage.
    /// Best for: Production search with high quality requirements.
    /// </summary>
    Hnsw = 1,

    /// <summary>
    /// Inverted File with Flat compression (IVFFlat) index.
    /// Pros: Fast index build, lower memory.
    /// Cons: Slightly lower recall than HNSW.
    /// Best for: Large datasets where build time matters.
    /// </summary>
    IvfFlat = 2
}

/// <summary>
/// Distance metrics supported by pgvector.
/// </summary>
public enum DistanceMetric
{
    /// <summary>
    /// Cosine distance: 1 - (a · b) / (||a|| * ||b||)
    /// Operator: &lt;=&gt;
    /// Best for: Normalized embeddings (most common).
    /// </summary>
    Cosine = 0,

    /// <summary>
    /// Euclidean (L2) distance: sqrt(sum((a_i - b_i)^2))
    /// Operator: &lt;-&gt;
    /// Best for: Non-normalized embeddings, spatial data.
    /// </summary>
    L2 = 1,

    /// <summary>
    /// Negative inner product: -(a · b)
    /// Operator: &lt;#&gt;
    /// Best for: Maximum inner product search (MIPS).
    /// </summary>
    InnerProduct = 2
}
