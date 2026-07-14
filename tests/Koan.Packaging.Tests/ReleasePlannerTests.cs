using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleasePlannerTests
{
    [Fact]
    public void ProjectReferencesResolveAgainstProjectFiles()
    {
        var root = Path.GetFullPath(Path.Combine("repo", "src"));
        var dependency = Project("Sylin.Koan.Core", Path.Combine(root, "Koan.Core"), "Koan.Core.csproj");
        var consumer = Project(
            "Sylin.Koan.App",
            Path.Combine(root, "Koan.App"),
            "Koan.App.csproj",
            Path.Combine(dependency.ProjectDirectory, "Koan.Core.csproj"));
        var projects = new Dictionary<string, PackageProject>(StringComparer.OrdinalIgnoreCase)
        {
            [ReleasePlanner.FullProjectPath(dependency)] = dependency,
            [ReleasePlanner.FullProjectPath(consumer)] = consumer
        };

        var dependencies = ReleasePlanner.ResolveProjectDependencies(consumer, projects);

        Assert.Equal(["Sylin.Koan.Core"], dependencies);
    }

    [Fact]
    public void TopologicalOrderPlacesDependenciesFirst()
    {
        var core = Package("Sylin.Koan.Core");
        var web = Package("Sylin.Koan.Web", core.PackageId);
        var app = Package("Sylin.Koan.App", web.PackageId);

        var result = ReleasePlanner.TopologicalOrder([app, core, web]);

        Assert.Equal([core.PackageId, web.PackageId, app.PackageId], result.Select(package => package.PackageId));
    }

    [Fact]
    public void TopologicalOrderRejectsCycles()
    {
        var left = Package("Sylin.Koan.Left", "Sylin.Koan.Right");
        var right = Package("Sylin.Koan.Right", "Sylin.Koan.Left");

        Assert.Throws<InvalidOperationException>(() => ReleasePlanner.TopologicalOrder([left, right]));
    }

    [Theory]
    [InlineData("[0.17.3, 0.18.0)", "0.17.3")]
    [InlineData("0.17.3", "0.17.3")]
    [InlineData("(, 1.0.0)", null)]
    public void MinimumVersionParsesNuGetRanges(string range, string? expected) =>
        Assert.Equal(expected, PackagePipeline.MinimumVersion(range));

    [Theory]
    [InlineData("[0.17.3, 0.18.0)", true)]
    [InlineData("[1.2.3, 2.0.0)", true)]
    [InlineData("0.17.3", false)]
    [InlineData("[0.17.3, )", false)]
    [InlineData("[0.17.3, 0.19.0)", false)]
    [InlineData("(0.17.3, 0.18.0)", false)]
    public void CompatibilityBandsAreExact(string range, bool expected) =>
        Assert.Equal(expected, PackagePipeline.IsExpectedCompatibilityBand(range));

    private static ReleasePackage Package(string id, params string[] dependencies) => new()
    {
        PackageId = id,
        Version = "0.17.1",
        ProjectPath = $"src/{id}/{id}.csproj",
        Kind = "Package",
        Reason = "test",
        IncludeSymbols = true,
        ProjectDependencies = dependencies.ToList()
    };

    private static PackageProject Project(
        string id,
        string directory,
        string projectFile,
        params string[] references) =>
        new(projectFile, directory, id, "Package", true, "README.md", "Description", "koan;test", references);
}
