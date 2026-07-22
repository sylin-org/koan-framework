using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ProductSurfaceCompilerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "koan-product-surface-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void KeepsAvailableButUnclaimedPackagesUnassessed()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var claimed = Project("Sylin.Koan.Claimed");
        var available = Project("Sylin.Koan.Available");

        var surface = Compiler().Compile([available, claimed], Claims(Claim(claimed.PackageId)));

        Assert.Single(surface.Claims);
        Assert.Empty(surface.Packages.Single(package => package.PackageId == available.PackageId).Claims);
        Assert.Equal(
            ["capability"],
            surface.Packages.Single(package => package.PackageId == claimed.PackageId).Claims);
    }

    [Fact]
    public void RejectsUnknownPackages()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([Project("Sylin.Koan.Core")], Claims(Claim("Sylin.Koan.Missing"))));

        Assert.Contains("Sylin.Koan.Missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateClaims()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var package = Project("Sylin.Koan.Core");

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([package], Claims(Claim(package.PackageId), Claim(package.PackageId))));

        Assert.Contains("more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownMaturity()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var package = Project("Sylin.Koan.Core");
        var claim = Claim(package.PackageId) with { Maturity = "probably-ready" };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([package], Claims(claim)));

        Assert.Contains("canonical maturity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMissingEvidence()
    {
        SeedPath("docs/capability.md");
        var package = Project("Sylin.Koan.Core");
        var claim = Claim(package.PackageId) with { Evidence = ["tests/missing.txt"] };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([package], Claims(claim)));

        Assert.Contains("tests/missing.txt", error.Message, StringComparison.Ordinal);
        Assert.Contains("evidence", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsSupportPromotionWithoutOwnedReadme()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var package = Project("Sylin.Koan.Core", ownsReadme: false);
        var claim = Claim(package.PackageId) with { Maturity = "supported-foundation" };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([package], Claims(claim)));

        Assert.Contains("owned README", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSupportPromotionWithout020VersionIntent()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var package = Project("Sylin.Koan.Core", versionIntent: "0.19");
        var claim = Claim(package.PackageId) with { Maturity = "supported-foundation" };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([package], Claims(claim)));

        Assert.Contains("version intent '0.19'", error.Message, StringComparison.Ordinal);
        Assert.Contains("0.20", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects020VersionIntentWithoutASupportedClaim()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var package = Project("Sylin.Koan.Core", versionIntent: "0.20");

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([package], Claims(Claim(package.PackageId))));

        Assert.Contains("0.20 version intent", error.Message, StringComparison.Ordinal);
        Assert.Contains("no supported claim", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsSupportedPackageWithAnUnsupportedPublicDependency()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var dependency = Project("Sylin.Koan.Core", versionIntent: "0.17");
        var application = Project(
            "Sylin.Koan.App",
            references: [Reference(dependency)],
            versionIntent: "0.20");
        var claim = Claim(application.PackageId) with { Maturity = "supported-foundation" };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Compiler().Compile([application, dependency], Claims(claim)));

        Assert.Contains(application.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains(dependency.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains("not owned by a supported claim", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsCompleteSupported020DependencyClosure()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var dependency = Project("Sylin.Koan.Core", versionIntent: "0.20");
        var application = Project(
            "Sylin.Koan.App",
            references: [Reference(dependency)],
            versionIntent: "0.20");
        var claim = Claim(application.PackageId) with
        {
            Maturity = "supported-foundation",
            Packages = [application.PackageId, dependency.PackageId]
        };

        var surface = Compiler().Compile([application, dependency], Claims(claim));

        Assert.All(surface.Packages, package => Assert.Equal("0.20", package.VersionIntent));
    }

    [Fact]
    public void ProducesDeterministicStandardPackageShapes()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var library = Project("Sylin.Koan.Library");
        var bundle = Project("Sylin.Koan.Bundle", includeBuildOutput: false, references: [Reference(library)]);
        var template = Project("Sylin.Koan.Template", packageType: "Template", includeBuildOutput: false);
        var analyzer = Project("Sylin.Koan.Analyzer", isRoslynComponent: true, includeBuildOutput: false);

        var first = Compiler().Compile(
            [template, library, analyzer, bundle],
            Claims(Claim(library.PackageId)));
        var second = Compiler().Compile(
            [bundle, analyzer, library, template],
            Claims(Claim(library.PackageId)));

        Assert.Equal(ProductSurfaceCompiler.ToJson(first), ProductSurfaceCompiler.ToJson(second));
        Assert.Contains("framework_version: v0.20.0", ProductSurfaceCompiler.ToMarkdown(first), StringComparison.Ordinal);
        Assert.Equal("library", first.Packages.Single(package => package.PackageId == library.PackageId).Shape);
        Assert.Equal("bundle", first.Packages.Single(package => package.PackageId == bundle.PackageId).Shape);
        Assert.Equal("template", first.Packages.Single(package => package.PackageId == template.PackageId).Shape);
        Assert.Equal("analyzer", first.Packages.Single(package => package.PackageId == analyzer.PackageId).Shape);
    }

    [Fact]
    public void EmitsTheCompleteCanonicalMaturityVocabulary()
    {
        SeedPath("docs/capability.md");
        SeedPath("tests/evidence.txt");
        var package = Project("Sylin.Koan.Library");

        var markdown = ProductSurfaceCompiler.ToMarkdown(
            Compiler().Compile([package], Claims(Claim(package.PackageId))));

        Assert.Contains("## Maturity vocabulary", markdown, StringComparison.Ordinal);
        Assert.Contains("not one linear ranking", markdown, StringComparison.Ordinal);
        foreach (var definition in PackagingConstants.ProductSurface.MaturityDefinitions)
        {
            Assert.Contains($"| `{definition.Name}` |", markdown, StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ProductSurfaceCompiler Compiler() => new(root);

    private static ProductClaims Claims(params ProductClaimInput[] claims) => new()
    {
        SchemaVersion = 1,
        Claims = claims.ToList()
    };

    private static ProductClaimInput Claim(string packageId) => new()
    {
        Id = "capability",
        Title = "Capability",
        Summary = "Business outcome.",
        Maturity = "verified",
        Packages = [packageId],
        Documentation = ["docs/capability.md"],
        Evidence = ["tests/evidence.txt"]
    };

    private PackageProject Project(
        string id,
        string packageType = "Dependency",
        bool isRoslynComponent = false,
        bool includeBuildOutput = true,
        bool ownsReadme = true,
        string[]? references = null,
        string versionIntent = "0.17")
    {
        var name = id.Replace('.', '-');
        var directory = Path.Combine(root, "src", name);
        Directory.CreateDirectory(directory);
        return new PackageProject(
            $"src/{name}/{name}.csproj",
            directory,
            id,
            packageType,
            ["net10.0"],
            false,
            isRoslynComponent,
            includeBuildOutput,
            false,
            true,
            "README.md",
            ownsReadme,
            ownsReadme ? $"src/{name}/TECHNICAL.md" : null,
            "Description",
            "koan;test",
            references ?? [],
            VersionIntent: versionIntent);
    }

    private static string Reference(PackageProject project) =>
        Path.Combine(project.ProjectDirectory, Path.GetFileName(project.ProjectPath));

    private void SeedPath(string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
    }
}
