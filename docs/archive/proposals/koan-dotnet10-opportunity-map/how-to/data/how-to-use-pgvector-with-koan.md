# Use PGVector for Semantic Search with Koan

**Contract**
- Inputs: Koan application targeting `net10.0`+, PostgreSQL 12+ with pgvector extension, `Koan.Data.Connector.PGVector` package at `0.7.0`+.
- Outputs: Vector embeddings stored in PostgreSQL tables with HNSW or IVFFlat indexing, semantic search API via `Vector<T>` facade.
- Error Modes: pgvector extension missing (installation guide provided), dimension mismatch (validation error), connection failures (standard PostgreSQL diagnostics).
- Acceptance Criteria: `Vector<T>.Save()` persists embeddings to `{entity}_vector` tables, `Vector<T>.Search()` returns semantically similar results ordered by cosine/L2/inner product distance.
- Edge Cases: Zero vectors stored (empty results), dimension > 16,000 (validation error), bulk operations > 10K vectors (batching required).

## Steps

1. **Add pgvector connector package reference.** Install the `Koan.Data.Connector.PGVector` NuGet package to your project. Auto-registration activates on package reference (Reference = Intent pattern):

    ```bash
    dotnet add package Koan.Data.Connector.PGVector --version 0.7.0
    ```

    - Package automatically registers `IVectorAdapterFactory`, extension manager, and telemetry.
    - No manual service registration required—Koan's auto-registrar handles it.

2. **Configure PostgreSQL connection and vector options.** Bind `Koan:Vector:PGVector` configuration section to control connection, indexing strategy, and performance tuning:

    ```json
    {
      "Koan": {
        "Vector": {
          "PGVector": {
            "ConnectionString": "Host=localhost;Port=5432;Database=myapp;Username=user;Password=pass",
            "DefaultDimension": 1536,
            "DefaultTopK": 10,
            "AutoCreateIndex": true,
            "DefaultIndexType": "Hnsw",
            "HnswM": 16,
            "HnswEfConstruction": 64,
            "DistanceMetric": "Cosine"
          }
        }
      }
    }
    ```

    - `DefaultDimension`: Embedding size (384=MiniLM, 768=BERT, 1536=OpenAI ada-002, 3072=OpenAI large).
    - `DefaultIndexType`: `Hnsw` (better recall, slower build) or `IvfFlat` (faster build, lower recall).
    - `DistanceMetric`: `Cosine` (normalized vectors), `L2` (Euclidean), or `InnerProduct` (MIPS).
    - Leave blank to use defaults (HNSW with cosine similarity).

3. **Mark entity for vector storage.** Apply `[VectorAdapter("pgvector")]` attribute to specify PGVector as the storage provider:

    ```csharp
    using Koan.Data.Abstractions;
    using Koan.Data.Vector.Abstractions;

    [VectorAdapter("pgvector")]
    public class Article : Entity<Article>
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Category { get; set; } = "";
    }
    ```

    - Attribute is optional—if omitted, Koan uses highest-priority vector adapter (PGVector priority 15).
    - Table name auto-includes `_vector` suffix per DATA-0087: `Article` → `article_vector`.

4. **Store vector embeddings.** Generate embeddings via AI provider and save using `Vector<T>` static API:

    ```csharp
    using Koan.Data.Vector;

    // Generate embedding (example with Koan.AI)
    var embedding = await Ai.FromText(article.Title + " " + article.Content)
        .WithSource("openai-embeddings")
        .ToEmbedding();

    // Save to PGVector
    await Vector<Article>.Save(article.Id, embedding.ToArray(), new
    {
        Category = article.Category,
        PublishedDate = DateTime.UtcNow
    });
    ```

    - Metadata stored as JSONB for filtering (e.g., `Category`, `PublishedDate`).
    - Upsert semantics: existing vectors updated, new vectors inserted.
    - Partition-aware: use `Vector<Article>.WithPartition("tenant-123")` for multi-tenancy.

5. **Perform semantic search.** Query similar vectors using cosine/L2/inner product similarity:

    ```csharp
    // Search for similar articles
    var queryEmbedding = await Ai.FromText("machine learning tutorial")
        .WithSource("openai-embeddings")
        .ToEmbedding();

    var results = await Vector<Article>.Search(
        vector: queryEmbedding.ToArray(),
        topK: 10,
        filter: new { Category = "Tech" } // Optional metadata filter
    );

    foreach (var result in results.Results)
    {
        Console.WriteLine($"{result.Id}: {result.Score:F3}");
        // Example: article-123: 0.927
    }
    ```

    - Results ordered by descending similarity (1.0 = identical, 0.0 = orthogonal).
    - Filters use PostgreSQL JSONB containment (`@>` operator).
    - Supports pagination via `ContinuationToken` (for future releases).

6. **Verify table and index creation.** Inspect PostgreSQL to confirm schema and indexing:

    ```bash
    # Connect to PostgreSQL
    psql -h localhost -U user -d myapp

    # Check table structure
    \d article_vector

    # Expected output:
    # Column     | Type                     | Modifiers
    #------------+--------------------------+-----------
    # id         | text                     | not null
    # embedding  | vector(1536)             |
    # metadata   | jsonb                    |
    # created_at | timestamp with time zone | default now()
    # updated_at | timestamp with time zone | default now()
    #
    # Indexes:
    #     "article_vector_pkey" PRIMARY KEY, btree (id)
    #     "article_vector_embedding_hnsw_idx" hnsw (embedding vector_cosine_ops)

    # Verify pgvector extension
    SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';
    # Expected: vector | 0.6.0 (or later)
    ```

    - Table name includes `_vector` suffix (DATA-0087 collision prevention).
    - Index type matches configuration (`hnsw` or `ivfflat`).
    - No collision with relational `article` table (if Entity<Article> also used).

7. **Optimize for production workloads.** Tune indexing parameters based on dataset size and quality requirements:

    **For large datasets (>1M vectors):**
    ```json
    {
      "DefaultIndexType": "IvfFlat",
      "IvfFlatLists": 1000
    }
    ```
    - Rule of thumb: `lists = sqrt(rows)`.
    - Faster index build, 95%+ recall with proper `lists` tuning.

    **For high-quality recall:**
    ```json
    {
      "DefaultIndexType": "Hnsw",
      "HnswM": 32,
      "HnswEfConstruction": 128
    }
    ```
    - Higher `m` and `ef_construction` = better recall, slower build.
    - Recommended for production search with <1M vectors.

    **Bulk loading optimization:**
    ```csharp
    // Disable auto-indexing during bulk load
    await Vector<Article>.EnsureCreated(); // Creates table without index

    // Bulk insert
    var items = /* thousands of embeddings */;
    await Vector<Article>.Save(items);

    // Create index after loading
    await using var conn = /* get Npgsql connection */;
    var indexBuilder = new PgVectorIndexBuilder(conn, options, logger);
    await indexBuilder.CreateHnswIndexAsync("article_vector");
    ```

8. **Monitor performance and health.** Use Koan's telemetry and PostgreSQL system views:

    ```csharp
    // Telemetry automatically tracked via ActivitySource
    // View in Application Insights, Jaeger, or Zipkin:
    // - vector.ensureCreated
    // - vector.upsert
    // - vector.search (latency, result count)

    // Index statistics
    await using var conn = /* get connection */;
    var stats = await indexBuilder.GetIndexStatsAsync("article_vector_embedding_hnsw_idx");
    Console.WriteLine($"Index size: {stats.Size}, Scans: {stats.Scans}");

    // Analyze table after bulk operations
    await indexBuilder.AnalyzeTableAsync("article_vector");
    ```

## Notes

- **pgvector extension installation**: If extension missing, admin must run `CREATE EXTENSION vector;` or install from https://github.com/pgvector/pgvector#installation.
- **Dimension limits**: pgvector v0.6.0+ supports up to 16,000 dimensions. For larger embeddings, use dimensionality reduction (PCA, UMAP).
- **Distance metric selection**: Cosine for normalized embeddings (most common), L2 for spatial data, InnerProduct for maximum inner product search (MIPS).
- **Partition support**: Use `Vector<Article>.WithPartition("tenant-id")` for multi-tenant isolation. Creates separate tables per partition: `article_vector#tenant1`, `article_vector#tenant2`.
- **Container orchestration**: Use official `pgvector/pgvector:pg16` Docker image. Koan auto-discovers via Aspire or Compose profiles.
- **Migration from other providers**: Use `Vector<T>.ExportAllAsync()` to stream vectors from Weaviate/Milvus, then bulk import to PGVector.
