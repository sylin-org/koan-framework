using Koan.Data.Abstractions;

namespace Koan.Core.Adapters;

/// <summary>
/// Extended capability flags that build on existing QueryCapabilities.
/// This enables runtime capability querying and dynamic feature detection.
/// </summary>
[Flags]
public enum ExtendedQueryCapabilities
{
    None = 0,
    VectorSearch = 1,
    SemanticSearch = 2,
    Embeddings = 4,
    FullTextSearch = 8,
    GraphTraversal = 16,
    SpatialQueries = 32,
    AnalyticsQueries = 64
}

[Flags]
public enum HealthCapabilities
{
    None = 0,
    Basic = 1,
    ConnectionHealth = 2,
    ResponseTime = 4,
    ResourceUsage = 8,
    ServiceDependencies = 16,
    CustomMetrics = 32
}

[Flags]
public enum ConfigurationCapabilities
{
    None = 0,
    EnvironmentVariables = 1,
    ConfigurationFiles = 2,
    ConnectionStrings = 4,
    OrchestrationAware = 8,
    HotReload = 16,
    Validation = 32
}

[Flags]
public enum SecurityCapabilities
{
    None = 0,
    Authentication = 1,
    Authorization = 2,
    Encryption = 4,
    TokenBased = 8,
    CertificateBased = 16,
    MutualTls = 32
}

[Flags]
public enum MessagingCapabilities
{
    None = 0,
    PublishSubscribe = 1,
    RequestResponse = 2,
    Queues = 4,
    Topics = 8,
    Streams = 16,
    Transactions = 32
}

[Flags]
public enum OrchestrationCapabilities
{
    None = 0,
    ContainerAware = 1,
    ServiceDiscovery = 2,
    LoadBalancing = 4,
    HealthChecks = 8,
    AutoScaling = 16,
    CircuitBreaker = 32
}