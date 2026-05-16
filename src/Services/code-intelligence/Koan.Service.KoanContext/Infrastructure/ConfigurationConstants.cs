namespace Koan.Service.KoanContext.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the KoanContext service.
/// Eliminates magic "Koan:" string literals across the context service.
/// </summary>
internal static class ConfigurationConstants
{
    public const string ContextSection = "Koan:Context";

    public static class IndexingPerformance
    {
        public const string Section = ContextSection + ":IndexingPerformance";
        public const string IndexingChunkSize = Section + ":IndexingChunkSize";
        public const string MaxFileSizeMB = Section + ":MaxFileSizeMB";
        public const string MaxConcurrentIndexingJobs = Section + ":MaxConcurrentIndexingJobs";
        public const string EmbeddingBatchSize = Section + ":EmbeddingBatchSize";
        public const string EnableParallelProcessing = Section + ":EnableParallelProcessing";
        public const string MaxDegreeOfParallelism = Section + ":MaxDegreeOfParallelism";
        public const string DefaultTokenBudget = Section + ":DefaultTokenBudget";
    }

    public static class FileMonitoring
    {
        public const string Section = ContextSection + ":FileMonitoring";
        public const string Enabled = Section + ":Enabled";
        public const string DebounceMilliseconds = Section + ":DebounceMilliseconds";
        public const string MaxConcurrentReindexOperations = Section + ":MaxConcurrentReindexOperations";
    }

    public static class ProjectResolution
    {
        public const string Section = ContextSection + ":ProjectResolution";
        public const string AutoCreate = Section + ":AutoCreate";
        public const string AutoIndex = Section + ":AutoIndex";
        public const string MaxSizeGB = Section + ":MaxSizeGB";
    }

    public static class JobMaintenance
    {
        public const string Section = ContextSection + ":JobMaintenance";
        public const string MaxJobsPerProject = Section + ":MaxJobsPerProject";
        public const string JobRetentionDays = Section + ":JobRetentionDays";
        public const string EnableAutomaticCleanup = Section + ":EnableAutomaticCleanup";
    }

    public static class Security
    {
        public const string Section = ContextSection + ":Security";
        public const string Headers = Section + ":Headers";
    }

    public static class Keys
    {
        public const string AutoResumeIndexing = ContextSection + ":AutoResumeIndexing";
        public const string AutoResumeDelay = ContextSection + ":AutoResumeDelay";
    }

    public static class Data
    {
        public const string SourcesDefaultAdapter = "Koan:Data:Sources:Default:Adapter";
        public const string SourcesDefaultConnectionString = "Koan:Data:Sources:Default:ConnectionString";
        public const string OrchestrationWeaviateHostPort = "Koan:Orchestration:Weaviate:HostPort";
    }

    public static class Weaviate
    {
        public const string Dimension = "Koan:Data:Weaviate:Dimension";
        public const string Metric = "Koan:Data:Weaviate:Metric";
        public const string DefaultTopK = "Koan:Data:Weaviate:DefaultTopK";
        public const string MaxTopK = "Koan:Data:Weaviate:MaxTopK";
        public const string TimeoutSeconds = "Koan:Data:Weaviate:TimeoutSeconds";
    }

    public static class Ai
    {
        public const string EmbeddingProvider = "Koan:AI:Embedding:Provider";
        public const string EmbeddingModel = "Koan:AI:Embedding:Model";
        public const string EmbeddingEndpoint = "Koan:AI:Embedding:Endpoint";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Context:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{ContextSection}:{key}";
}
