namespace Koan.Packaging.Infrastructure;

internal static class PackagingConstants
{
    public const string PackagePrefix = "Sylin.Koan";
    public const string PreviewFrameworkVersion = "v0.20.0";
    public const string CorePackageId = "Sylin.Koan.Core";
    public const string MsBuildDisableNodeReuseEnvironmentVariable = "MSBUILDDISABLENODEREUSE";
    public const string MsBuildDisableNodeReuseEnvironmentValue = "1";
    public const int EvaluationParallelism = 8;

    public static class PackageValidation
    {
        public const string PreviewVersionPrefix = "0.20.";
        public const string NuGetFlatContainerBaseUrl = "https://api.nuget.org/v3-flatcontainer/";
        public const string NuGetVersionsIndexFile = "index.json";
    }

    public static class Admission
    {
        public const int DefaultDeadlineSeconds = 300;
        public const int MinimumDeadlineSeconds = 1;
        public const int MaximumDeadlineSeconds = 3600;
        public const string DeterministicLane = "deterministic";
        public const string NativeLane = "native";
        public const string ExecutionPhase = "execution";
        public const string PassedVerdict = "passed";
        public const string FailedVerdict = "failed";
        public const string RequiredApplicability = "required";
        public const string NotApplicable = "not-applicable";
        public const string TrxExtension = ".trx";
        public const string CellIdPattern = "^[a-z0-9]+(?:[.:_-][a-z0-9]+)*$";
        public static readonly IReadOnlySet<string> SharedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "NuGet.Config",
            ProductSurface.ClaimsPath,
            ".github/workflows/canary-nightly.yml",
            "scripts/forge-verify.ps1",
            "scripts/test-bootstrap.ps1"
        };
        public static readonly IReadOnlyList<string> SharedPrefixes =
        [
            "tools/Koan.Packaging/",
            ".github/actions/",
            "eng/",
            "src/Koan.Testing.",
            "tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/",
            "tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/"
        ];
    }

    public static class ProductSurface
    {
        public const int Schema = 1;
        public const string ClaimsPath = "product/claims.json";
        public const string GeneratedJsonPath = "docs/reference/product-surface.json";
        public const string GeneratedMarkdownPath = "docs/reference/product-surface.md";
        public const string UnassessedMaturity = "unassessed";
        public static readonly IReadOnlyList<(string Name, string Meaning, string Contract)> MaturityDefinitions =
        [
            ("supported-foundation",
                "An admitted part of Koan's recommended application base with documented limits and terminal evidence.",
                "Its owner and public Koan dependencies carry the 0.20 patch-compatibility promise."),
            ("supported-extension",
                "An admitted optional capability with documented prerequisites, limits, and terminal evidence.",
                "Its owner and public Koan dependencies carry the 0.20 patch-compatibility promise."),
            ("verified",
                "Focused executable evidence covers the claim's stated boundary.",
                "Evidence is current, but the claim has not been admitted to the 0.20 support promise."),
            ("demonstrated",
                "At least one executable path shows the capability working within stated limits.",
                "The path is useful evidence, not a support or patch-compatibility promise."),
            ("experimental",
                "An implemented capability is available for evaluation while its public shape or guarantees may change.",
                "Expect revision; do not rely on 0.20 compatibility unless a separate supported claim says otherwise."),
            ("specified",
                "The intended public outcome is documented, but terminal implementation or external proof remains pending.",
                "Treat it as planned contract evidence, not an available support promise."),
            ("unassessed",
                "A package is present but has no accepted product claim evaluating its public contract.",
                "Package availability alone creates no maturity, support, or compatibility promise."),
            ("deprecated",
                "A transition surface remains available but is no longer the recommended current path.",
                "Move to the documented replacement; continued availability is not guaranteed beyond its stated window."),
            ("retired",
                "The capability is outside the current product surface.",
                "Do not begin or continue new use through this path."),
        ];

        public static readonly IReadOnlySet<string> Maturities = MaturityDefinitions
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);
        public static readonly IReadOnlySet<string> PromotedMaturities = new HashSet<string>(StringComparer.Ordinal)
        {
            "supported-extension",
            "supported-foundation"
        };
    }

    public static class TerminalOutcomes
    {
        public const int Schema = 1;
        public const int BaselineCount = 55;
        public const string ArchitectureDecision = "ARCH-0120";
        public const string ArchitectureDecisionPath = "docs/decisions/ARCH-0120-terminal-package-maturity.md";
        public const string CertificatePath = "docs/initiatives/koan-v1/R13-TERMINAL-OUTCOMES.json";
        public const string BaselineTableHeader = "| # | Wave | Package owner | Intended decision surface |";
        public static readonly IReadOnlySet<string> RemovedDispositions = new HashSet<string>(StringComparer.Ordinal)
        {
            "absorbed",
            "migrated",
            "retired"
        };
    }

    public static class PackageQuality
    {
        public const int Schema = 1;
        public const string Source = "evaluated-msbuild-package-graph";
        public const string AssessmentDate = "2026-07-17";
        public const string RepairRequiredStatus = "repair-required";
        public const string ReviewRequiredStatus = "review-required";
        public const string StructurallyReadyStatus = "structurally-ready";
        public const string ErrorSeverity = "error";
        public const string WarningSeverity = "warning";
        public const string EntryRole = "entry";
        public const string FoundationRole = "foundation";
        public const string ContractsRole = "contracts";
        public const string ProviderRole = "provider";
        public const string ProjectionRole = "projection";
        public const string CapabilityRole = "capability";
        public const int TerseDescriptionCharacters = 60;
        public const string CanonicalIcon = "icon.png";

        public static readonly IReadOnlySet<string> HistoricalUniversalTags = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "aspnetcore", "cqrs", "ddd", "messaging", "data", "opentelemetry"
        };

        public static class Findings
        {
            public const string MissingDescription = "metadata.description.missing";
            public const string TerseDescription = "metadata.description.terse";
            public const string MissingTags = "metadata.tags.missing";
            public const string GenericTags = "metadata.tags.generic";
            public const string MissingProjectUrl = "metadata.project-url.missing";
            public const string MissingRepository = "metadata.repository.missing";
            public const string MissingLicense = "metadata.license.missing";
            public const string MissingTargetFramework = "metadata.target-framework.missing";
            public const string GenericReleaseNotes = "metadata.release-notes.generic";
            public const string MissingIcon = "identity.icon.missing";
            public const string NonCanonicalIcon = "identity.icon.noncanonical";
            public const string MissingOwnedReadme = "docs.readme.owned.missing";
            public const string MissingPackageTitle = "docs.readme.package-title.missing";
            public const string MissingInstall = "docs.readme.install.missing";
            public const string MissingMeaningfulUse = "docs.readme.meaningful-use.missing";
            public const string MissingBoundaries = "docs.readme.boundaries.missing";
            public const string MissingTechnical = "docs.technical.missing";
        }
    }

}
