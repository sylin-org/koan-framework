using Newtonsoft.Json.Linq;

namespace Koan.Data.SearchEngine;

/// <summary>
/// The per-backend seam between the shared <see cref="SearchEngineVectorRepository{TEntity,TKey}"/>
/// and a concrete search engine (Elasticsearch 8.x vs OpenSearch 2.x). DATA-0103 established that the
/// two connectors are ~79% byte-identical: every piece of REST/transport wiring (HttpClient config,
/// auth, NDJSON bulk envelope, <c>_doc</c> PUT/DELETE, <c>_delete_by_query</c>, probe-then-create index
/// flow, hit parsing, scroll export) is identical. ONLY three things genuinely differ, and they are
/// exactly the three members of this interface:
/// <list type="number">
///   <item>the kNN search-request JSON body (ES top-level <c>knn</c> vs OS <c>query.knn.&lt;field&gt;</c>),</item>
///   <item>the index settings+mapping JSON body (ES <c>dense_vector</c> vs OS <c>knn_vector</c>+method),</item>
///   <item>the similarity-metric token mapping (ES passes the raw token; OS maps to a <c>space_type</c>).</item>
/// </list>
/// Plus a human-readable <see cref="EngineLabel"/> used both in error messages and as the
/// <see cref="SearchEngineFilterTranslator"/> engine argument so a failure names the actual adapter.
/// </summary>
public interface ISearchEngineDialect
{
    /// <summary>"Elasticsearch" | "OpenSearch" — used in error messages AND as the
    /// <see cref="SearchEngineFilterTranslator"/> engine argument.</summary>
    string EngineLabel { get; }

    /// <summary>
    /// Build the engine-native kNN search-request body. The shared base supplies the already-translated
    /// metadata <paramref name="filter"/> (a Lucene-DSL <see cref="JObject"/>, or null when no filter)
    /// and adds the <c>timeout</c> afterwards — the dialect must NOT add timeout.
    /// </summary>
    JObject BuildSearchRequestBody(float[] query, int topK, JToken? filter, ISearchEngineVectorOptions opts);

    /// <summary>
    /// Build the engine-native index creation body (settings + mappings) for a vector field of the given
    /// <paramref name="dimension"/>, using the already-mapped <paramref name="mappedSimilarity"/> token
    /// (the result of <see cref="MapSimilarityToken"/>).
    /// </summary>
    JObject BuildIndexBody(int dimension, string mappedSimilarity, ISearchEngineVectorOptions opts);

    /// <summary>
    /// Map the cross-provider similarity-metric token onto the engine-native token. Elasticsearch puts
    /// the raw token straight into <c>similarity</c> (identity); OpenSearch maps it onto a KNN
    /// <c>space_type</c>.
    /// </summary>
    string MapSimilarityToken(string metric);
}
