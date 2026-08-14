namespace Koan.Packaging.Infrastructure;

internal static class PackagingConstants
{
    public const string PackagePrefix = "Sylin.Koan";
    public const string PreviewFrameworkVersion = "v0.20.0";
    public const string CorePackageId = "Sylin.Koan.Core";
    public const string MsBuildDisableNodeReuseEnvironmentVariable = "MSBUILDDISABLENODEREUSE";
    public const string MsBuildDisableNodeReuseEnvironmentValue = "1";
    public const int EvaluationParallelism = 8;

    public static class ProductSurface
    {
        public const int ClaimsSchema = 1;
        public const int Schema = 2;
        public const string ClaimsPath = "product/claims.json";
        public const string GeneratedMarkdownPath = "docs/reference/product-surface.md";
        public const string UnassessedMaturity = "unassessed";
        public static readonly IReadOnlyList<(string Name, string Meaning, string Contract)> MaturityDefinitions =
        [
            ("supported-foundation",
                "An admitted part of Koan's recommended application base with documented limits and terminal evidence.",
                "The capability carries its documented support guarantee in addition to the train-wide 1.x compatibility contract."),
            ("supported-extension",
                "An admitted optional capability with documented prerequisites, limits, and terminal evidence.",
                "The capability carries its documented support guarantee in addition to the train-wide 1.x compatibility contract."),
            ("verified",
                "Focused executable evidence covers the claim's stated boundary.",
                "The package remains 1.x compatible; the behavioral guarantee is limited to the stated evidence."),
            ("demonstrated",
                "At least one executable path shows the capability working within stated limits.",
                "The package remains 1.x compatible; the demonstrated path is not a broader operational guarantee."),
            ("experimental",
                "An implemented capability is available for evaluation while its behavior or guarantees may evolve.",
                "Evolution within 1.x remains compatible; a breaking public shape waits for the next major train."),
            ("specified",
                "The intended public outcome is documented, but terminal implementation or external proof remains pending.",
                "The package remains 1.x compatible, but this outcome is planned rather than an available guarantee."),
            ("unassessed",
                "A package is present but has no accepted product claim evaluating its public contract.",
                "The package carries the 1.x compatibility contract but no assessed behavioral guarantee."),
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
        public static readonly IReadOnlySet<string> SupportedMaturities = new HashSet<string>(StringComparer.Ordinal)
        {
            "supported-extension",
            "supported-foundation"
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
