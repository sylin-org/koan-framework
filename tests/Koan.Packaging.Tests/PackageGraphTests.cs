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
    public void ReverseClosureIncludesRootsAndAllTransitiveDependents()
    {
        var core = Project("Sylin.Koan.Core");
        var data = Project("Sylin.Koan.Data", Reference(core));
        var app = Project("Sylin.Koan.App", Reference(data));
        var unrelated = Project("Sylin.Koan.Unrelated");
        var graph = new PackageGraph([unrelated, app, data, core]);

        var closure = graph.ReverseDependentClosure([core.PackageId]);

        Assert.Equal([core.PackageId, data.PackageId, app.PackageId], closure);
        Assert.DoesNotContain(unrelated.PackageId, closure);
    }

    [Fact]
    public void ReverseClosureUnionsMultipleRootsDeterministically()
    {
        var left = Project("Sylin.Koan.Left");
        var right = Project("Sylin.Koan.Right");
        var bridge = Project("Sylin.Koan.Bridge", Reference(left), Reference(right));
        var graph = new PackageGraph([bridge, right, left]);

        var closure = graph.ReverseDependentClosure([right.PackageId, left.PackageId]);

        Assert.Equal([left.PackageId, right.PackageId, bridge.PackageId], closure);
    }

    [Fact]
    public void RejectsUnknownRoots()
    {
        var graph = new PackageGraph([Project("Sylin.Koan.Core")]);

        var error = Assert.Throws<InvalidOperationException>(() =>
            graph.ReverseDependentClosure(["Sylin.Koan.Missing"]));

        Assert.Contains("Sylin.Koan.Missing", error.Message, StringComparison.Ordinal);
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

    private static PackageProject Project(string id, params string[] references)
    {
        var name = id.Replace('.', '-');
        var directory = Path.Combine(Path.GetTempPath(), "koan-package-graph-tests", name);
        return new PackageProject(
            $"src/{name}/{name}.csproj",
            directory,
            id,
            "Package",
            true,
            "README.md",
            "Description",
            "koan;test",
            references,
            []);
    }
}
