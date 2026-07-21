using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Vector.Connector.Weaviate.Infrastructure;

internal static class WeaviateProvenanceItems
{
    internal static readonly string[] ConnectionStringKeys =
    {
        Constants.Configuration.Keys.ConnectionString,
        "ConnectionStrings:Weaviate"
    };

    internal static readonly string[] EndpointKeys =
    {
        Constants.Configuration.Keys.Endpoint
    };

    internal static readonly string[] ApiKeyKeys =
    {
        Constants.Configuration.Keys.ApiKey
    };

    internal static readonly string[] MetricKeys =
    {
        Constants.Configuration.Keys.Metric
    };

    internal static readonly string[] TimeoutKeys =
    {
        Constants.Configuration.Keys.TimeoutSeconds
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
