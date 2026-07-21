using System;
using System.Linq;
using Koan.Data.SearchEngine;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.ElasticSearch;

/// <summary>
/// Elasticsearch 8.x flavour of the shared search-engine vector seam (DATA-0103): top-level <c>knn</c>
/// query (field/query_vector/k/num_candidates) with the metadata filter pushed into <c>knn.filter</c>,
/// a <c>dense_vector</c> mapping with a top-level <c>similarity</c> token, and identity similarity-token
/// mapping (ES accepts the raw token).
/// </summary>
internal sealed class ElasticSearchDialect : ISearchEngineDialect
{
    public string EngineLabel => "Elasticsearch";

    public JObject BuildSearchRequestBody(float[] query, int topK, JToken? filter, ISearchEngineVectorOptions opts)
    {
        var request = new JObject
        {
            ["size"] = topK,
            ["knn"] = new JObject
            {
                ["field"] = opts.VectorField,
                ["query_vector"] = new JArray(query.Select(v => (double)v)),
                ["k"] = topK,
                ["num_candidates"] = Math.Max(topK, topK * 2)
            },
            ["_source"] = new JArray(opts.MetadataField, opts.IdField)
        };

        if (filter is not null)
        {
            // DATA-0097 F6: the filter must PRE-FILTER the kNN (knn.filter), not sit as a top-level
            // query sibling — a sibling query is OR-combined with knn in ES 8.x, so the filter would
            // not constrain the vector results (it returned the full top-K unfiltered).
            ((JObject)request["knn"]!)["filter"] = filter;
        }

        return request;
    }

    public JObject BuildIndexBody(int dimension, string mappedSimilarity, ISearchEngineVectorOptions opts) => new()
    {
        ["settings"] = new JObject
        {
            ["index"] = new JObject
            {
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
                    ["type"] = "dense_vector",
                    ["dims"] = dimension,
                    ["index"] = true,
                    ["similarity"] = mappedSimilarity
                },
                [opts.MetadataField] = new JObject { ["type"] = "object", ["dynamic"] = true }
            }
        }
    };

    // Elasticsearch puts the raw cross-provider token straight into "similarity" — identity mapping.
    public string MapSimilarityToken(string metric) => metric;
}
