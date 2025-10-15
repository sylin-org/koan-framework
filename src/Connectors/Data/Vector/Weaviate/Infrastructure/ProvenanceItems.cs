using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Vector.Connector.Weaviate.Infrastructure;

internal static class WeaviateProvenanceItems
{
    internal static readonly string[] ConnectionStringKeys =
    {
        "Koan:Data:Weaviate:ConnectionString",
        "Koan:Data:ConnectionString",
        "ConnectionStrings:Weaviate",
        "ConnectionStrings:weaviate"
    };

    internal static readonly string[] EndpointKeys =
    {
        "Koan:Data:Weaviate:Endpoint",
        "Koan:Data:Weaviate:BaseUrl",
        "Weaviate:Endpoint"
    };

    internal static readonly string[] ApiKeyKeys =
    {
        "Koan:Data:Weaviate:ApiKey",
        "Koan:Data:Weaviate:Key"
    };

    internal static readonly string[] DefaultTopKKeys =
    {
        "Koan:Data:Weaviate:DefaultTopK"
    };

    internal static readonly string[] MaxTopKKeys =
    {
        "Koan:Data:Weaviate:MaxTopK"
    };

    internal static readonly string[] DimensionKeys =
    {
        "Koan:Data:Weaviate:Dimension"
    };

    internal static readonly string[] MetricKeys =
    {
        "Koan:Data:Weaviate:Metric"
    };

    internal static readonly string[] TimeoutKeys =
    {
        "Koan:Data:Weaviate:TimeoutSeconds"
    };

    private static readonly IReadOnlyCollection<string> ConnectionConsumers = new[]
    {
        "Koan.Data.Vector.Connector.Weaviate.WeaviateOptionsConfigurator",
        "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
    };

    private static readonly IReadOnlyCollection<string> VectorConsumers = new[]
    {
        "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
    };

    internal static readonly ProvenanceItem ConnectionString = new(
        ConnectionStringKeys[0],
        "Weaviate Connection String",
        "Weaviate connection string resolved from configuration, discovery, or defaults.",
        IsSecret: false,
        MustSanitize: true,
        DefaultConsumers: ConnectionConsumers);

    internal static readonly ProvenanceItem Endpoint = new(
        EndpointKeys[0],
        "Weaviate Endpoint",
        "Endpoint used for Weaviate vector operations.",
        DefaultConsumers: ConnectionConsumers);

    internal static readonly ProvenanceItem DefaultTopK = new(
        DefaultTopKKeys[0],
        "Default Top K",
        "Default vector search result size for Weaviate queries.",
        DefaultConsumers: VectorConsumers);

    internal static readonly ProvenanceItem MaxTopK = new(
        MaxTopKKeys[0],
        "Max Top K",
        "Maximum vector search result size enforced by Weaviate adapter.",
        DefaultConsumers: VectorConsumers);

    internal static readonly ProvenanceItem Dimension = new(
        DimensionKeys[0],
        "Vector Dimension",
        "Expected vector dimension when embedding data for Weaviate.",
        DefaultConsumers: VectorConsumers);

    internal static readonly ProvenanceItem Metric = new(
        MetricKeys[0],
        "Similarity Metric",
        "Similarity metric applied for Weaviate vector search (e.g., cosine).",
        DefaultConsumers: VectorConsumers);

    internal static readonly ProvenanceItem TimeoutSeconds = new(
        TimeoutKeys[0],
        "Request Timeout Seconds",
        "Default timeout (in seconds) used for Weaviate requests.",
        DefaultConsumers: VectorConsumers);
}
