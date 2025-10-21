namespace Koan.Samples.Meridian.Infrastructure;

/// <summary>
/// Stable identifiers and routes for the Meridian sample.
/// Centralizing literals keeps controllers and services clean.
/// </summary>
public static class MeridianConstants
{
    public const string StorageProfile = "meridian";
    public const string StorageContainer = "pipelines";

    public const string VectorProfile = "meridian:evidence";

    public static class Pipelines
    {
        public const string Route = "api/pipelines";
    }

    public static class Documents
    {
        public const string Route = "api/pipelines/{pipelineId}/documents";
    }

    public static class Jobs
    {
        public const string Route = "api/pipelines/{pipelineId}/jobs";
    }

    public static class Deliverables
    {
        public const string Route = "api/pipelines/{pipelineId}/deliverables";
    }

    public static class SourceTypeCatalog
    {
        public const string Route = "api/sourcetypes";
        public const string AiSuggestSegment = "ai-suggest";
    }

    public static class AnalysisTypeCatalog
    {
        public const string Route = "api/analysistypes";
        public const string AiSuggestSegment = "ai-suggest";
    }

    public static class SourceTypes
    {
        public const string AuditedFinancial = "AuditedFinancial";
        public const string VendorPrescreen = "VendorPrescreen";
        public const string KnowledgeBase = "KnowledgeBase";
        public const string Unclassified = "Unclassified";
    }
}
