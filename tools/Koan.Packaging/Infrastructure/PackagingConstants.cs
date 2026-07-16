namespace Koan.Packaging.Infrastructure;

internal static class PackagingConstants
{
    public const string ManifestFileName = "release-set.json";
    public const string StateFileName = "release-state.json";
    public const string LineageArtifactFileName = "release-lineage.json";
    public const string LineageStateFileName = ".koan-package-lineage.json";
    public const string LineageMarkerFileName = ".koan-package-lineage-marker.json";
    public const string DefaultLineageBranch = "automation/package-lineage-dev";
    public const string LineageCommitterName = "Koan Release Automation";
    public const string LineageCommitterEmail = "release-automation@koan.dev";
    public const string VersionChangedReason = "version-changed";
    public const string BreakingRootReason = "breaking-root";
    public const string BreakingDependentReason = "breaking-dependent";
    public const string SharedPackageInputReason = "shared-package-input";
    public const string LineageBootstrapReason = "lineage-bootstrap";
    public const string RegistryRepairReason = "unpublished-current-version";
    public const string NuGetSource = "https://api.nuget.org/v3/index.json";
    public const string NuGetFlatContainer = "https://api.nuget.org/v3-flatcontainer";
    public const string PackagePrefix = "Sylin.Koan";
    public const string CorePackageId = "Sylin.Koan.Core";
    public const string CoreCompositionTargetPackagePath = "buildTransitive/Sylin.Koan.Core.targets";
    public const string DefaultBeforeRevision = "HEAD~1";
    public const string DefaultAfterRevision = "HEAD";
    public const string MsBuildDisableNodeReuseEnvironmentVariable = "MSBUILDDISABLENODEREUSE";
    public const string MsBuildDisableNodeReuseEnvironmentValue = "1";
    public const int EvaluationParallelism = 8;
    public const int PublishAttempts = 5;
    public const int RegistryAttempts = 20;
    public const int RegistryHttpAttempts = 5;
    public const int ReleaseManifestSchema = 3;
    public const int ReleaseLineageSchema = 3;

    public static class ApplicationProbe
    {
        public const string HealthPath = "health/ready";
        public const string FactsPath = ".well-known/Koan/facts";
        public const string McpPath = "mcp";
        public const string McpSessionHeader = "Mcp-Session-Id";
        public const string McpProtocolVersion = "2025-06-18";
        public const string RuntimeFactsUri = "koan://facts";
        public const string CompositionLockfileName = "koan.lock.json";
        public const string CompositionSchemaProperty = "schema";
        public const string CompositionAppProperty = "app";
        public const string CompositionNameProperty = "name";
        public const string CompositionVersionProperty = "koan";
        public const string CompositionTargetFrameworkProperty = "tfm";
        public const string CompositionModulesProperty = "modules";
        public const string CompositionModuleIdProperty = "id";
        public const string CompositionDirectReferencesProperty = "directReferences";
        public const string CompositionReferenceKindProperty = "kind";
        public const string CompositionReferenceIdProperty = "id";
        public const string FactsProperty = "facts";
        public const string FactCodeProperty = "code";
        public const string FactSummaryProperty = "summary";
        public const string LockfileMatchedCode = "koan.composition.lockfile.matched";
        public const string LockfileDriftedCode = "koan.composition.lockfile.drifted";
        public const string UnknownCompositionVersion = "0.0";
        public const string CoreModuleId = "Koan.Core";
        public const string SqliteModuleId = "Koan.Data.Connector.Sqlite";
        public const string McpModuleId = "Koan.Mcp";
        public const string JobsModuleId = "Koan.Jobs";
        public const string CommunicationModuleId = "Koan.Communication";
        public const string TargetFramework = "net10.0";
        public const int CompositionLockfileSchema = 2;
        public const string SelfUri = "koan://self";
        public const string CustomToolsProperty = "customTools";
        public const string EmptySelfMessage = "nothing here you can use yet";
        public const string MissingWebRootWarning = "The WebRootPath was not found";
        public const string MissingMcpMetadataWarning = "Missing metadata for";
        public const int StartupAttempts = 40;
        public const int StartupPollMilliseconds = 500;
        public const int HttpTimeoutSeconds = 5;
        public const int RejectedObservationMilliseconds = 2500;
        public const int MaximumWorkerIterationErrors = 1;
    }

    public static class FirstUse
    {
        public const string ProjectFile = "FirstUse.csproj";
        public const string ApplicationName = "Koan.FirstUse";
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
        public const string ApprovalSubject = "Approve supplier invoice";
        public const string AdapterSelectedCode = "koan.data.adapter.selected";
        public const string DefaultDataSubject = "data:default";
        public const int StartupAttempts = ApplicationProbe.StartupAttempts;
        public const int StartupPollMilliseconds = ApplicationProbe.StartupPollMilliseconds;
        public const int HttpTimeoutSeconds = ApplicationProbe.HttpTimeoutSeconds;
        public static readonly IReadOnlyList<string> SourceDirectReferences =
        [
            "project|Koan.Data.Connector.Sqlite",
            "project|Koan.Mcp",
            "project|Sylin.Koan.App",
        ];
        public static readonly IReadOnlyList<string> PackageDirectReferences =
        [
            "package|Sylin.Koan.App",
            "package|Sylin.Koan.Data.Connector.Sqlite",
            "package|Sylin.Koan.Mcp",
        ];
    }

    public static class GoldenJourney
    {
        public const string ProjectFile = "GoldenJourney.csproj";
        public const string ApplicationName = "Koan.GoldenJourney";
        public const string EvidenceFileName = "golden-journey-package-evidence.json";
        public const string ReviewsPath = "api/reviews";
        public const string PendingTool = "review_pending";
        public const string RecommendTool = "review_recommend";
        public const string JobsLedgerSubject = "jobs:ledger";
        public const string JobsWakeSubject = "jobs:wake";
        public const string DefaultDataSubject = "data:default";
        public const string AdapterRejectedCode = "koan.data.adapter.rejected";
        public const string AdapterSelectedCode = "koan.data.adapter.selected";
        public const string AdapterUnavailableReason = "adapter-unavailable";
        public const string UnavailableAdapter = "not-referenced";
        public const string SqliteAdapter = "sqlite";
        public const string DurableLedger = "durable-data";
        public const string InProcessCommunication = "in-process";
        public const string NotReadyOutcome = "review.not-ready";
        public const string AcceptedOutcome = "review.recommendation-recorded";
        public static readonly IReadOnlyList<string> SourceDirectReferences =
        [
            "project|Koan.Data.Connector.Sqlite",
            "project|Koan.Jobs",
            "project|Koan.Mcp",
            "project|Sylin.Koan.App",
        ];
        public static readonly IReadOnlyList<string> PackageDirectReferences =
        [
            "package|Sylin.Koan.App",
            "package|Sylin.Koan.Data.Connector.Sqlite",
            "package|Sylin.Koan.Jobs",
            "package|Sylin.Koan.Mcp",
        ];
    }
}
