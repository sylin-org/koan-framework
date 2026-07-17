using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageQualityCompilerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "koan-package-quality-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DerivesRolesAndKeepsPureContractsProportional()
    {
        var contracts = Project(
            "Sylin.Koan.Billing.Contracts",
            technical: false,
            description: "Inert billing vocabulary shared by applications and functional Koan modules.");
        var provider = Project(
            "Sylin.Koan.Billing.Connector.Acme",
            references: [Reference(contracts)],
            description: "Acme-backed billing provider with explicit capability and availability reporting.");

        var report = Compiler().Compile([provider, contracts]);

        Assert.Equal(PackagingConstants.PackageQuality.ContractsRole, Assessment(report, contracts).Role);
        Assert.Equal(PackagingConstants.PackageQuality.ProviderRole, Assessment(report, provider).Role);
        Assert.DoesNotContain(
            Assessment(report, contracts).Findings,
            code => code == PackagingConstants.PackageQuality.Findings.MissingTechnical);
        Assert.Equal(
            PackagingConstants.PackageQuality.StructurallyReadyStatus,
            Assessment(report, contracts).Status);
        Assert.Equal([contracts.PackageId], Assessment(report, provider).Dependencies);
    }

    [Fact]
    public void ReportsRootReadmeFallbackAsRepairRatherThanPackageDocumentation()
    {
        var package = Project(
            "Sylin.Koan.MissingDocs",
            ownsReadme: false,
            description: "A deliberately undocumented package used to prove corrective quality reporting.");

        var report = Compiler().Compile([package]);
        var assessment = Assessment(report, package);

        Assert.Equal(PackagingConstants.PackageQuality.RepairRequiredStatus, assessment.Status);
        Assert.Null(assessment.Readme);
        Assert.Contains(
            assessment.Findings,
            code => code == PackagingConstants.PackageQuality.Findings.MissingOwnedReadme);
        Assert.Contains(
            report.FindingDefinitions,
            finding => finding.Code == PackagingConstants.PackageQuality.Findings.MissingOwnedReadme &&
                       finding.Severity == PackagingConstants.PackageQuality.ErrorSeverity);
    }

    [Fact]
    public void TreatsHistoricalUniversalTagsAndLegacyIconAsReviewSignals()
    {
        var package = Project(
            "Sylin.Koan.Review",
            tags: "koan;framework;dotnet;aspnetcore;cqrs;ddd;messaging;data;opentelemetry;review",
            icon: "0_2.jpg",
            description: "A package with mechanically valid but overly broad discovery and identity metadata.");

        var assessment = Assessment(Compiler().Compile([package]), package);

        Assert.Equal(PackagingConstants.PackageQuality.ReviewRequiredStatus, assessment.Status);
        Assert.Contains(
            assessment.Findings,
            code => code == PackagingConstants.PackageQuality.Findings.GenericTags);
        Assert.Contains(
            assessment.Findings,
            code => code == PackagingConstants.PackageQuality.Findings.NonCanonicalIcon);
    }

    [Fact]
    public void ProducesDeterministicMachineAndHumanReports()
    {
        var firstPackage = Project(
            "Sylin.Koan.Alpha",
            description: "Alpha capability with one explicit package intent and a complete consumer contract.");
        var secondPackage = Project(
            "Sylin.Koan.Beta",
            description: "Beta capability with one explicit package intent and a complete consumer contract.");

        var first = Compiler().Compile([secondPackage, firstPackage]);
        var second = Compiler().Compile([firstPackage, secondPackage]);

        Assert.Equal(PackageQualityCompiler.ToJson(first), PackageQualityCompiler.ToJson(second));
        Assert.Equal(PackageQualityCompiler.ToMarkdown(first), PackageQualityCompiler.ToMarkdown(second));
        Assert.Equal([firstPackage.PackageId, secondPackage.PackageId], first.Packages.Select(package => package.PackageId));
        Assert.Contains($"[`{firstPackage.ProjectPath}`]", PackageQualityCompiler.ToMarkdown(first), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private PackageQualityCompiler Compiler() => new(root);

    private PackageProject Project(
        string id,
        string description,
        bool ownsReadme = true,
        bool technical = true,
        string tags = "koan;dotnet;capability",
        string icon = PackagingConstants.PackageQuality.CanonicalIcon,
        string packageType = "Dependency",
        bool packAsTool = false,
        bool isRoslynComponent = false,
        bool includeBuildOutput = true,
        string[]? references = null)
    {
        var name = id.Replace('.', '-');
        var directory = Path.Combine(root, "src", name);
        Directory.CreateDirectory(directory);
        var projectPath = $"src/{name}/{name}.csproj";
        File.WriteAllText(Path.Combine(root, projectPath), "<Project />");
        if (ownsReadme)
        {
            File.WriteAllText(
                Path.Combine(directory, "README.md"),
                $"""
                # {id}

                ## Install

                `dotnet add package {id}`

                ## Use

                This package provides its named meaningful result.

                ## Guarantees and limits

                The package states its bounded guarantee and correction.
                """);
        }
        if (technical) File.WriteAllText(Path.Combine(directory, "TECHNICAL.md"), $"# {id} technical contract");

        return new PackageProject(
            projectPath,
            directory,
            id,
            packageType,
            ["net10.0"],
            packAsTool,
            isRoslynComponent,
            includeBuildOutput,
            false,
            true,
            "README.md",
            ownsReadme,
            technical ? $"src/{name}/TECHNICAL.md" : null,
            description,
            tags,
            references ?? [],
            [],
            icon,
            "https://koan.dev",
            "https://github.com/sylin-org/koan-framework.git",
            "Apache-2.0",
            "Package-specific change intent." );
    }

    private static string Reference(PackageProject project) =>
        Path.Combine(project.ProjectDirectory, Path.GetFileName(project.ProjectPath));

    private static PackageQualityAssessment Assessment(PackageQualityReport report, PackageProject project) =>
        report.Packages.Single(package => package.PackageId == project.PackageId);
}
