namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Capability declaration that gates per-spec execution for vector adapters. Adapters declare
/// what they support; specs that need a missing capability skip cleanly with a clear reason.
/// </summary>
/// <remarks>
/// Defaults assume a fully-featured general-purpose vector store (Weaviate-ish). Adapters
/// override the relevant flags to false when they don't support an operation. The matrix's
/// job is to verify each adapter's CLAIMED capabilities actually work — not to force every
/// adapter to support every operation.
/// </remarks>
public interface IVectorAdapterCapabilities
{
    /// <summary>
    /// IVectorSearchRepository.GetEmbedding / GetEmbeddings — id-based vector retrieval.
    /// Weaviate and PGVector implement; Milvus, ElasticSearch, OpenSearch do not by default.
    /// </summary>
    bool SupportsGetEmbedding => true;

    /// <summary>
    /// IVectorSearchRepository.UpsertMany / DeleteMany — batched writes/deletes.
    /// </summary>
    bool SupportsBulkOperations => true;

    /// <summary>
    /// IVectorSearchRepository.Flush — destructive clear of the underlying index.
    /// All current adapters override the default-throws implementation.
    /// </summary>
    bool SupportsFlush => true;

    /// <summary>
    /// IVectorSearchRepository.ExportAll — streaming export for backup/migration.
    /// Weaviate, ElasticSearch, PGVector implement; OpenSearch and Milvus do not.
    /// </summary>
    bool SupportsExportAll => true;

    /// <summary>
    /// The <c>vector.index.stats</c> instruction (<c>Vector&lt;T&gt;.Stats()</c>) — count of stored vectors.
    /// Implemented by the search-engine connectors (ES/OS, shared base), Weaviate, and the in-memory
    /// reference; Qdrant and Milvus do not expose it through the instruction surface. (Distinct from
    /// <see cref="SupportsExportAll"/> — an adapter can stream-export without implementing the count instruction.)
    /// </summary>
    bool SupportsIndexStats => true;

    /// <summary>
    /// VectorQueryOptions.SearchText + Alpha — hybrid (semantic + BM25) search.
    /// Weaviate only.
    /// </summary>
    bool SupportsHybridSearch => false;

    /// <summary>
    /// VectorQueryOptions.Filter — provider-specific metadata filter applied during search.
    /// All five adapters implement, but the filter format differs (Weaviate GraphQL WHERE,
    /// Milvus expr, PGVector JSONB containment, ES/OS query DSL).
    /// </summary>
    bool SupportsMetadataFilters => true;

    /// <summary>
    /// VectorQueryResult.ContinuationToken — opaque pagination tokens.
    /// Weaviate (cursor) and ElasticSearch (scroll) return non-null tokens; others do not.
    /// </summary>
    bool SupportsContinuationToken => false;

    /// <summary>
    /// EntityContext.Partition() routing produces isolated indexes/collections/tables/scopes.
    /// All five adapters support this, but via different native primitives.
    /// </summary>
    bool SupportsPartitionIsolation => true;

    /// <summary>
    /// Adapter can create vector collections/indexes/tables on the fly when a new partition
    /// is encountered (rather than requiring up-front schema declaration).
    /// </summary>
    bool SupportsDynamicCollections => true;

    /// <summary>
    /// Search returns scores in a consistent normalized range across query/data combinations.
    /// Cosine-similarity adapters typically satisfy this; raw L2 distance does not.
    /// </summary>
    bool SupportsScoreNormalization => false;

    /// <summary>
    /// A successful Delete is immediately reflected in subsequent Search results from the same
    /// caller. Most adapters satisfy this trivially. Milvus 2.4 REST is a known exception:
    /// search runs against growing segments where filter-based deletes don't land until
    /// segments seal, and the REST API exposes no flush/compact endpoint to force the issue.
    /// Query (point-lookup) sees the delete immediately; Search (KNN) does not.
    /// </summary>
    bool SupportsDeleteImmediatelyVisibleToSearch => true;
}
