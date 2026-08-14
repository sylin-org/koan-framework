using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageGraphTests
{
    [Fact]
    public void ResolvesOnlyPackableProjectReferences()
    {
        var core = Project("Sylin.Koan.Core");
        var app = Project(
            "Sylin.Koan.App",
            Reference(core),
            Path.Combine(Path.GetTempPath(), "koan-tests", "Unpackable", "Unpackable.csproj"));

        var graph = new PackageGraph([app, core]);

        Assert.Equal([core.PackageId], graph.DependenciesOf(app.PackageId));
    }

    [Fact]
    public void OrdersSelectedPackagesDependencyFirst()
    {
        var core = Project("Sylin.Koan.Core");
        var web = Project("Sylin.Koan.Web", Reference(core));
        var app = Project("Sylin.Koan.App", Reference(web));
        var graph = new PackageGraph([app, core, web]);

        var ordered = graph.TopologicalOrder([app.PackageId, core.PackageId, web.PackageId]);

        Assert.Equal([core.PackageId, web.PackageId, app.PackageId], ordered);
    }

    [Fact]
    public void SuppressedPackageDependenciesRemainSourceReferences()
    {
        var core = Project("Sylin.Koan.Core");
        var tool = Project("Sylin.Koan.Tool", suppressDependenciesWhenPacking: true, Reference(core));
        var graph = new PackageGraph([tool, core]);

        Assert.Equal([core.PackageId], graph.DependenciesOf(tool.PackageId));
        Assert.Empty(graph.PackageDependenciesOf(tool.PackageId));
    }

    [Fact]
    public void RejectsPackageCyclesAtConstruction()
    {
        var left = Project("Sylin.Koan.Left");
        var right = Project("Sylin.Koan.Right", Reference(left));
        left = Project(left.PackageId, Reference(right));

        var error = Assert.Throws<InvalidOperationException>(() => new PackageGraph([left, right]));

        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Reference(PackageProject project) =>
        Path.Combine(project.ProjectDirectory, Path.GetFileName(project.ProjectPath));

    private static PackageProject Project(string id, params string[] references) =>
        Project(id, suppressDependenciesWhenPacking: false, references);

    private static PackageProject Project(
        string id,
        bool suppressDependenciesWhenPacking,
        params string[] references)
    {
        var name = id.Replace('.', '-');
        var directory = Path.Combine(Path.GetTempPath(), "koan-package-graph-tests", name);
        return new PackageProject(
            $"src/{name}/{name}.csproj",
            directory,
            id,
            "Dependency",
            ["net10.0"],
            false,
            false,
            true,
            suppressDependenciesWhenPacking,
            true,
            "README.md",
            true,
            "TECHNICAL.md",
            "Description",
            "koan;test",
            references);
    }
}
