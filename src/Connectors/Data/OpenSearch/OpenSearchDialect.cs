using System.Linq;
using Koan.Data.SearchEngine;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.OpenSearch;

/// <summary>
/// OpenSearch 2.x flavour of the shared search-engine vector seam (DATA-0103). The KNN query shape
/// differs sharply from Elasticsearch's 8.x:
///   { "query": { "knn": { "&lt;field&gt;": { "vector": [...], "k": N, "filter"?: {...} } } } }
/// Elasticsearch's top-level "knn" with "field"/"query_vector" is rejected by OS with
/// "Unknown key for a START_OBJECT in [knn]". The index requires <c>index.knn = true</c> plus a
/// <c>knn_vector</c> field with a method config (engine + space_type), and the cross-provider
/// similarity token is mapped onto an OS <c>space_type</c>.
/// </summary>
internal sealed class OpenSearchDialect : ISearchEngineDialect
{
    public string EngineLabel => "OpenSearch";

    public JObject BuildSearchRequestBody(float[] query, int topK, JToken? filter, ISearchEngineVectorOptions opts)
    {
        // OpenSearch 2.x KNN query shape (differs from Elasticsearch's 8.x):
        //   { "query": { "knn": { "<field>": { "vector": [...], "k": N, "filter"?: {...} } } } }
        // Elasticsearch's top-level "knn" with "field"/"query_vector" is rejected by OS with
        // "Unknown key for a START_OBJECT in [knn]".
        var knnFieldBody = new JObject
        {
            ["vector"] = new JArray(query.Select(v => (double)v)),
            ["k"] = topK
        };

        if (filter is not null)
        {
            knnFieldBody["filter"] = new JObject
            {
                ["bool"] = new JObject
                {
                    ["filter"] = new JArray(filter)
                }
            };
        }

        return new JObject
        {
            ["size"] = topK,
            ["query"] = new JObject
            {
                ["knn"] = new JObject
                {
                    [opts.VectorField] = knnFieldBody
                }
            },
            ["_source"] = new JArray(opts.MetadataField, opts.IdField)
        };
    }

    public JObject BuildIndexBody(int dimension, string mappedSimilarity, ISearchEngineVectorOptions opts) => new()
    {
        // OpenSearch 2.x KNN index requires `index.knn = true` in settings and `knn_vector`
        // field type with a method config (engine + space_type). This differs sharply from
        // Elasticsearch's `dense_vector` + top-level `similarity` model.
        ["settings"] = new JObject
        {
            ["index"] = new JObject
            {
                ["knn"] = true,
                ["number_of_shards"] = 1,
                ["number_of_replicas"] = 0
            }
        },
        ["mappings"] = new JObject
        {
            ["properties"] = new JObject
            {
                [opts.IdField] = new JObject { ["type"] = "keyword" },
                [opts.VectorField] = new JObject
                {
                    ["type"] = "knn_vector",
                    ["dimension"] = dimension,
                    ["method"] = new JObject
                    {
                        ["name"] = "hnsw",
                        ["engine"] = "lucene",
                        ["space_type"] = mappedSimilarity
                    }
                },
                [opts.MetadataField] = new JObject { ["type"] = "object", ["dynamic"] = true }
            }
        }
    };

    /// <summary>
    /// Map the cross-provider similarity metric token onto OpenSearch's KNN space_type values.
    /// OS supports: l2, cosinesimil, innerproduct, l1, linf, hamming, hammingbit. The framework's
    /// Options field accepts the Elasticsearch-friendly tokens ("cosine", "l2", "dotproduct")
    /// so adapters look the same to users; we translate at the API boundary.
    /// </summary>
    public string MapSimilarityToken(string metric) => metric?.ToLowerInvariant() switch
    {
        "cosine" or "cosinesimil" => "cosinesimil",
        "l2" or "euclidean"        => "l2",
        "dot" or "dotproduct" or "innerproduct" => "innerproduct",
        "l1"                       => "l1",
        "linf"                     => "linf",
        _                          => "cosinesimil"
    };
}
