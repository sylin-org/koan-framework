namespace Koan.Packaging.Infrastructure;

internal static class PackagingConstants
{
    public const string ManifestFileName = "release-set.json";
    public const string StateFileName = "release-state.json";
    public const string NuGetSource = "https://api.nuget.org/v3/index.json";
    public const string NuGetFlatContainer = "https://api.nuget.org/v3-flatcontainer";
    public const string PackagePrefix = "Sylin.Koan";
    public const string DefaultBeforeRevision = "HEAD~1";
    public const string DefaultAfterRevision = "HEAD";
    public const int EvaluationParallelism = 8;
    public const int PublishAttempts = 5;
    public const int RegistryAttempts = 20;
    public const int RegistryHttpAttempts = 5;

    public static class ApplicationProbe
    {
        public const string HealthPath = "health/ready";
        public const string FactsPath = ".well-known/Koan/facts";
        public const string McpPath = "mcp";
        public const string McpSessionHeader = "Mcp-Session-Id";
        public const string McpProtocolVersion = "2025-06-18";
        public const string RuntimeFactsUri = "koan://facts";
        public const string SelfUri = "koan://self";
        public const string CustomToolsProperty = "customTools";
        public const string EmptySelfMessage = "nothing here you can use yet";
        public const string MissingWebRootWarning = "The WebRootPath was not found";
        public const int StartupAttempts = 40;
        public const int StartupPollMilliseconds = 500;
        public const int HttpTimeoutSeconds = 5;
    }

    public static class FirstUse
    {
        public const string ProjectFile = "FirstUse.csproj";
        public const string EvidenceFileName = "first-use-package-evidence.json";
        public const string HealthPath = ApplicationProbe.HealthPath;
        public const string FactsPath = ApplicationProbe.FactsPath;
        public const string ApprovalsPath = "api/approvals";
        public const string McpPath = ApplicationProbe.McpPath;
        public const string McpSessionHeader = ApplicationProbe.McpSessionHeader;
        public const string McpProtocolVersion = ApplicationProbe.McpProtocolVersion;
        public const string EntityCatalogUri = "koan://entities";
        public const string RuntimeFactsUri = ApplicationProbe.RuntimeFactsUri;
        public const string ApprovalEntity = "approval";
        public const string AdapterSelectedCode = "koan.data.adapter.selected";
        public const string DefaultDataSubject = "data:default";
        public const int StartupAttempts = ApplicationProbe.StartupAttempts;
        public const int StartupPollMilliseconds = ApplicationProbe.StartupPollMilliseconds;
        public const int HttpTimeoutSeconds = ApplicationProbe.HttpTimeoutSeconds;
    }

    public static class GoldenJourney
    {
        public const string ProjectFile = "GoldenJourney.csproj";
        public const string EvidenceFileName = "golden-journey-package-evidence.json";
        public const string ReviewsPath = "api/reviews";
        public const string PendingTool = "review_pending";
        public const string RecommendTool = "review_recommend";
        public const string JobsLedgerSubject = "jobs:ledger";
        public const string JobsTransportSubject = "jobs:transport";
        public const string DefaultDataSubject = "data:default";
        public const string AdapterRejectedCode = "koan.data.adapter.rejected";
        public const string AdapterSelectedCode = "koan.data.adapter.selected";
        public const string AdapterUnavailableReason = "adapter-unavailable";
        public const string UnavailableAdapter = "not-referenced";
        public const string SqliteAdapter = "sqlite";
        public const string DurableLedger = "durable-data";
        public const string InProcessTransport = "in-process";
        public const string NotReadyOutcome = "review.not-ready";
        public const string AcceptedOutcome = "review.recommendation-recorded";
    }
}
