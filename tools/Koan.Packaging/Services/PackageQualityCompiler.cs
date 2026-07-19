using System.Text;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class PackageQualityCompiler(string repositoryRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] MeaningfulUseHeadings =
    [
        "use", "usage", "example", "first result", "meaningful result", "getting started", "quickstart",
        "what it adds", "behavior", "host behavior", "contract"
    ];

    private static readonly string[] BoundaryHeadings =
    [
        "limit", "unsupported", "when not", "boundar", "guarantee", "failure", "correction", "requirements"
    ];

    private static readonly IReadOnlyDictionary<string, PackageQualityFinding> Definitions =
        new Dictionary<string, PackageQualityFinding>(StringComparer.Ordinal)
        {
            [PackagingConstants.PackageQuality.Findings.MissingDescription] = Error(
                PackagingConstants.PackageQuality.Findings.MissingDescription,
                "The package description is missing.",
                "Add an outcome-oriented Description to the owning project."),
            [PackagingConstants.PackageQuality.Findings.TerseDescription] = Warning(
                PackagingConstants.PackageQuality.Findings.TerseDescription,
                "The package description may be too terse to explain its distinct reference intent.",
                "Describe what capability appears, for whom, and the important boundary without repeating framework-wide marketing."),
            [PackagingConstants.PackageQuality.Findings.MissingTags] = Error(
                PackagingConstants.PackageQuality.Findings.MissingTags,
                "Package tags are missing.",
                "Add truthful package-specific discovery tags."),
            [PackagingConstants.PackageQuality.Findings.GenericTags] = Warning(
                PackagingConstants.PackageQuality.Findings.GenericTags,
                "The package inherits the complete historical framework-wide tag set.",
                "Keep only universally true shared tags and let this package own its capability-specific discovery terms."),
            [PackagingConstants.PackageQuality.Findings.MissingProjectUrl] = Error(
                PackagingConstants.PackageQuality.Findings.MissingProjectUrl,
                "Package project URL is missing.",
                "Set PackageProjectUrl through the shared packaging policy."),
            [PackagingConstants.PackageQuality.Findings.MissingRepository] = Error(
                PackagingConstants.PackageQuality.Findings.MissingRepository,
                "Package repository URL is missing.",
                "Set RepositoryUrl through the shared packaging policy."),
            [PackagingConstants.PackageQuality.Findings.MissingLicense] = Error(
                PackagingConstants.PackageQuality.Findings.MissingLicense,
                "Package license expression is missing.",
                "Set a standard SPDX PackageLicenseExpression through the shared packaging policy."),
            [PackagingConstants.PackageQuality.Findings.MissingTargetFramework] = Error(
                PackagingConstants.PackageQuality.Findings.MissingTargetFramework,
                "A library package has no evaluated target framework.",
                "Declare TargetFramework or TargetFrameworks in the owning SDK project."),
            [PackagingConstants.PackageQuality.Findings.GenericReleaseNotes] = Warning(
                PackagingConstants.PackageQuality.Findings.GenericReleaseNotes,
                "Release notes point only to the framework-wide release page.",
                "Compile package-specific change intent from Git/release facts while retaining a durable exact-wave link."),
            [PackagingConstants.PackageQuality.Findings.MissingIcon] = Error(
                PackagingConstants.PackageQuality.Findings.MissingIcon,
                "The package declares no embedded icon.",
                "Pack the canonical Koan mascot and set PackageIcon through the shared packaging policy."),
            [PackagingConstants.PackageQuality.Findings.NonCanonicalIcon] = Warning(
                PackagingConstants.PackageQuality.Findings.NonCanonicalIcon,
                "The package does not use the canonical Koan mascot.",
                $"Pack the repository-owned mascot as {PackagingConstants.PackageQuality.CanonicalIcon}."),
            [PackagingConstants.PackageQuality.Findings.MissingOwnedReadme] = Error(
                PackagingConstants.PackageQuality.Findings.MissingOwnedReadme,
                "The package falls back to the framework root README.",
                "Add a package-owned README that explains this reference intent and its smallest meaningful result."),
            [PackagingConstants.PackageQuality.Findings.MissingPackageTitle] = Warning(
                PackagingConstants.PackageQuality.Findings.MissingPackageTitle,
                "The README does not use the exact package ID as its primary title.",
                "Use the exact package ID as the primary title so the NuGet page and source companion identify the same product."),
            [PackagingConstants.PackageQuality.Findings.MissingInstall] = Warning(
                PackagingConstants.PackageQuality.Findings.MissingInstall,
                "The README does not show the package-specific install or reference expression.",
                "Add the shortest supported dotnet add/tool/template installation command and state any current publication boundary."),
            [PackagingConstants.PackageQuality.Findings.MissingMeaningfulUse] = Warning(
                PackagingConstants.PackageQuality.Findings.MissingMeaningfulUse,
                "The README has no recognizable meaningful-use or behavior section.",
                "Show the smallest honest result and explain what referencing the package makes available."),
            [PackagingConstants.PackageQuality.Findings.MissingBoundaries] = Warning(
                PackagingConstants.PackageQuality.Findings.MissingBoundaries,
                "The README has no recognizable guarantees, requirements, failure, or limitation section.",
                "State defaults, prerequisites, corrective failures, and unsupported guarantees proportionate to the package role."),
            [PackagingConstants.PackageQuality.Findings.MissingTechnical] = Warning(
                PackagingConstants.PackageQuality.Findings.MissingTechnical,
                "No package-owned technical companion describes runtime or build ownership.",
                "Add TECHNICAL.md when this package owns non-trivial activation, lifecycle, provider, projection, or build behavior; otherwise confirm during R11 review that the README contains the complete contract.")
        };

    public PackageQualityReport Compile(IReadOnlyCollection<PackageProject> projects)
    {
        var graph = new PackageGraph(projects);
        var packages = graph.Projects
            .OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(project => Assess(project, graph))
            .ToList();

        return new PackageQualityReport
        {
            Source = PackagingConstants.PackageQuality.Source,
            Summary = new PackageQualitySummary(
                packages.Count,
                packages.Count(package => package.Status == PackagingConstants.PackageQuality.RepairRequiredStatus),
                packages.Count(package => package.Status == PackagingConstants.PackageQuality.ReviewRequiredStatus),
                packages.Count(package => package.Status == PackagingConstants.PackageQuality.StructurallyReadyStatus),
                packages.Count(package => package.Readme is not null),
                packages.Count(package => package.TechnicalDocumentation is not null),
                packages.Sum(package => package.Findings.Count)),
            FindingDefinitions = packages
                .SelectMany(package => package.Findings)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(Definition)
                .ToList(),
            Packages = packages
        };
    }

    public static string ToJson(PackageQualityReport report) =>
        JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;

    public static string ToMarkdown(PackageQualityReport report)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("---");
        markdown.AppendLine("type: REFERENCE");
        markdown.AppendLine("domain: framework");
        markdown.AppendLine("title: \"Koan Package Quality\"");
        markdown.AppendLine("audience: [developers, maintainers, support-engineers, architects, ai-agents]");
        markdown.AppendLine("status: current");
        markdown.Append("last_updated: ").AppendLine(PackagingConstants.PackageQuality.AssessmentDate);
        markdown.Append("framework_version: ").AppendLine(PackagingConstants.PreviewFrameworkVersion);
        markdown.AppendLine("validation:");
        markdown.Append("  date_last_tested: ").AppendLine(PackagingConstants.PackageQuality.AssessmentDate);
        markdown.AppendLine("  status: generated");
        markdown.AppendLine("  scope: evaluated package structure and review signals; not support maturity");
        markdown.AppendLine("---");
        markdown.AppendLine();
        markdown.AppendLine("# Koan package quality");
        markdown.AppendLine();
        markdown.AppendLine("> Generated by `Koan.Packaging quality` from evaluated MSBuild/NuGet facts and package-owned docs. Do not edit by hand.");
        markdown.AppendLine();
        markdown.AppendLine("This report separates objective package repairs from human review. `structurally-ready` is not a graduation or support claim; R11 architecture, prose, and consumer evidence remain required.");
        markdown.AppendLine();
        markdown.AppendLine("## Summary");
        markdown.AppendLine();
        markdown.AppendLine("| Packages | Repair required | Review required | Structurally ready | Owned READMEs | Technical companions | Findings |");
        markdown.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        markdown.Append("| ").Append(report.Summary.Packages)
            .Append(" | ").Append(report.Summary.RepairRequired)
            .Append(" | ").Append(report.Summary.ReviewRequired)
            .Append(" | ").Append(report.Summary.StructurallyReady)
            .Append(" | ").Append(report.Summary.OwnedReadmes)
            .Append(" | ").Append(report.Summary.TechnicalCompanions)
            .Append(" | ").Append(report.Summary.Findings).AppendLine(" |");
        markdown.AppendLine();
        markdown.AppendLine("## Packages");
        markdown.AppendLine();
        markdown.AppendLine("| Package / owner | Role / shape | Structure | Package docs | Findings |");
        markdown.AppendLine("|---|---|---|---|---|");
        foreach (var package in report.Packages)
        {
            var docs = package.Readme is null
                ? "root README fallback"
                : $"[README](../../{package.Readme})" +
                  (package.TechnicalDocumentation is null
                      ? string.Empty
                      : $"<br>[TECHNICAL](../../{package.TechnicalDocumentation})");
            var findings = package.Findings.Count == 0
                ? "none"
                : string.Join("<br>", package.Findings.Select(code =>
                {
                    var finding = Definition(code);
                    return $"`{Escape(finding.Code)}` ({Escape(finding.Severity)}): {Escape(finding.Summary)}";
                }));
            markdown.Append("| `").Append(Escape(package.PackageId)).Append("`<br>")
                .Append("[`").Append(Escape(package.ProjectPath)).Append("`](../../")
                .Append(package.ProjectPath).Append(") | ")
                .Append(Escape(package.Role)).Append(" / ").Append(Escape(package.Shape))
                .Append(package.TargetFrameworks.Count == 0
                    ? string.Empty
                    : $"<br>{string.Join(", ", package.TargetFrameworks.Select(Escape))}")
                .Append(" | `")
                .Append(Escape(package.Status)).Append("` | ").Append(docs).Append(" | ")
                .Append(findings).AppendLine(" |");
        }

        return markdown.ToString();
    }

    private PackageQualityAssessment Assess(PackageProject project, PackageGraph graph)
    {
        var shape = PackageClassifier.ShapeOf(project, graph);
        var role = PackageClassifier.RoleOf(project, graph);
        var findings = new List<string>();

        AssessMetadata(project, findings);
        var readme = AssessReadme(project, findings);
        var technical = RepositoryPathIfExists(project.TechnicalDocumentation);
        if (technical is null && RequiresTechnicalCompanion(role, shape))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingTechnical);
        }

        findings.Sort(StringComparer.Ordinal);
        var status = findings.Any(code => Definition(code).Severity == PackagingConstants.PackageQuality.ErrorSeverity)
            ? PackagingConstants.PackageQuality.RepairRequiredStatus
            : findings.Any(code => Definition(code).Severity == PackagingConstants.PackageQuality.WarningSeverity)
                ? PackagingConstants.PackageQuality.ReviewRequiredStatus
                : PackagingConstants.PackageQuality.StructurallyReadyStatus;

        return new PackageQualityAssessment(
            project.PackageId,
            project.ProjectPath.Replace('\\', '/'),
            shape,
            role,
            status,
            project.Description,
            project.TargetFrameworks,
            graph.PackageDependenciesOf(project.PackageId),
            readme,
            technical,
            project.PackageIcon,
            findings);
    }

    private static void AssessMetadata(PackageProject project, ICollection<string> findings)
    {
        if (string.IsNullOrWhiteSpace(project.Description))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingDescription);
        }
        else if (project.Description.Trim().Length < PackagingConstants.PackageQuality.TerseDescriptionCharacters)
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.TerseDescription);
        }

        var tags = project.PackageTags.Split(
            [';', ' ', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tags.Length == 0)
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingTags);
        }
        else if (PackagingConstants.PackageQuality.HistoricalUniversalTags.All(tag =>
                     tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.GenericTags);
        }

        if (string.IsNullOrWhiteSpace(project.PackageProjectUrl))
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingProjectUrl);
        if (string.IsNullOrWhiteSpace(project.RepositoryUrl))
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingRepository);
        if (string.IsNullOrWhiteSpace(project.PackageLicenseExpression))
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingLicense);
        if (project.IncludeBuildOutput && project.TargetFrameworks.Count == 0)
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingTargetFramework);

        if (string.IsNullOrWhiteSpace(project.PackageIcon))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingIcon);
        }
        else if (!string.Equals(
                     project.PackageIcon,
                     PackagingConstants.PackageQuality.CanonicalIcon,
                     StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.NonCanonicalIcon);
        }

        if (!string.IsNullOrWhiteSpace(project.PackageReleaseNotes) &&
            project.PackageReleaseNotes.Contains("/releases", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.GenericReleaseNotes);
        }
    }

    private string? AssessReadme(PackageProject project, ICollection<string> findings)
    {
        if (!project.OwnsReadme || string.IsNullOrWhiteSpace(project.Readme))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingOwnedReadme);
            return null;
        }

        var fullPath = Path.Combine(project.ProjectDirectory, project.Readme);
        if (!File.Exists(fullPath))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingOwnedReadme);
            return null;
        }

        var content = File.ReadAllText(fullPath);
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!lines.Any(line => string.Equals(line, $"# {project.PackageId}", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingPackageTitle);
        }

        if (!ContainsInstallInstruction(content, project))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingInstall);
        }

        if (!ContainsHeading(lines, MeaningfulUseHeadings))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingMeaningfulUse);
        }

        if (!ContainsHeading(lines, BoundaryHeadings))
        {
            findings.Add(PackagingConstants.PackageQuality.Findings.MissingBoundaries);
        }

        return RepositoryPath(fullPath);
    }

    private static bool ContainsInstallInstruction(string content, PackageProject project)
    {
        if (project.PackAsTool)
        {
            return content.Contains($"dotnet tool install {project.PackageId}", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains($"dotnet tool install --global {project.PackageId}", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains($"dotnet tool install -g {project.PackageId}", StringComparison.OrdinalIgnoreCase);
        }

        var command = project.PackageType.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("Template", StringComparer.OrdinalIgnoreCase)
            ? $"dotnet new install {project.PackageId}"
            : $"dotnet add package {project.PackageId}";
        return content.Contains(command, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsHeading(IEnumerable<string> lines, IEnumerable<string> terms) =>
        lines.Where(line => line.StartsWith("##", StringComparison.Ordinal))
            .Any(line => terms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase)));

    private static bool RequiresTechnicalCompanion(string role, string shape) =>
        role != PackagingConstants.PackageQuality.ContractsRole && shape != "content";

    private string? RepositoryPathIfExists(string? path) =>
        string.IsNullOrWhiteSpace(path) || !File.Exists(Path.Combine(repositoryRoot, path))
            ? null
            : RepositoryPath(Path.Combine(repositoryRoot, path));

    private string RepositoryPath(string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    private static PackageQualityFinding Error(string code, string summary, string correction) =>
        new(code, PackagingConstants.PackageQuality.ErrorSeverity, summary, correction);

    private static PackageQualityFinding Warning(string code, string summary, string correction) =>
        new(code, PackagingConstants.PackageQuality.WarningSeverity, summary, correction);

    private static PackageQualityFinding Definition(string code) =>
        Definitions.TryGetValue(code, out var finding)
            ? finding
            : throw new InvalidOperationException($"Package quality finding '{code}' has no canonical definition.");

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
