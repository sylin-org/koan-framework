using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleaseLineageCompilerTests
{
    [Theory]
    [InlineData("0.17", "0.17", false)]
    [InlineData("0.17", "0.18", true)]
    [InlineData("0.17", "1.0", true)]
    [InlineData("1.2", "1.3", false)]
    [InlineData("1.2", "2.0", true)]
    public void BreakingTierFollowsKoanCompatibilityBands(string previous, string current, bool expected) =>
        Assert.Equal(expected, ReleaseLineageCompiler.IsBreakingTierAdvance(previous, current));

    [Theory]
    [InlineData("0.18", "0.17")]
    [InlineData("2.0", "1.9")]
    public void VersionIntentCannotMoveBackward(string previous, string current)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.IsBreakingTierAdvance(previous, current));

        Assert.Contains("backward", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkerPlanTouchesOnlyOtherwiseUnchangedClosureMembers()
    {
        var core = Project("Sylin.Koan.Core");
        var data = Project("Sylin.Koan.Data", Reference(core));
        var app = Project("Sylin.Koan.App", Reference(data));
        var unrelated = Project("Sylin.Koan.Unrelated");
        var graph = new PackageGraph([unrelated, app, data, core]);

        var plan = ReleaseLineageCompiler.Plan(
            graph,
            [core.PackageId],
            Versions(core, data, app, unrelated),
            Versions(core, data, app, unrelated, fresh: [core.PackageId, data.PackageId]));

        var markerId = Assert.Single(plan.MarkerPackages);
        var marker = Assert.Single(plan.Triggers, trigger => trigger.PackageId == markerId);
        Assert.Equal(app.PackageId, marker.PackageId);
        Assert.Equal([core.PackageId], marker.BreakingRoots);
    }

    [Fact]
    public void MarkerPlanRecordsEveryRootThatReachesThePackage()
    {
        var left = Project("Sylin.Koan.Left");
        var right = Project("Sylin.Koan.Right");
        var bridge = Project("Sylin.Koan.Bridge", Reference(left), Reference(right));
        var graph = new PackageGraph([bridge, right, left]);

        var plan = ReleaseLineageCompiler.Plan(
            graph,
            [right.PackageId, left.PackageId],
            Versions(left, right, bridge),
            Versions(left, right, bridge, fresh: [left.PackageId, right.PackageId]));

        var markerId = Assert.Single(plan.MarkerPackages);
        var marker = Assert.Single(plan.Triggers, trigger => trigger.PackageId == markerId);
        Assert.Equal(bridge.PackageId, marker.PackageId);
        Assert.Equal([left.PackageId, right.PackageId], marker.BreakingRoots);
    }

    [Fact]
    public void PreviousPackageDeletionFailsLoudly()
    {
        var core = Project("Sylin.Koan.Core");
        var graph = new PackageGraph([core]);
        var previous = new[]
        {
            new ReleaseLineagePackage(core.PackageId, core.ProjectPath),
            new ReleaseLineagePackage("Sylin.Koan.Removed", "src/removed/removed.csproj")
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.ValidatePackageContinuity(previous, graph));

        Assert.Contains("Sylin.Koan.Removed", error.Message, StringComparison.Ordinal);
        Assert.Contains("deletion", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviousPackageRenameFailsLoudly()
    {
        var current = Project("Sylin.Koan.Core");
        var graph = new PackageGraph([current]);
        var previous = new[]
        {
            new ReleaseLineagePackage(current.PackageId, "src/old/Koan.Core.csproj")
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.ValidatePackageContinuity(previous, graph));

        Assert.Contains("rename", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(current.PackageId, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewPackageIsAllowedByContinuityGuard()
    {
        var core = Project("Sylin.Koan.Core");
        var app = Project("Sylin.Koan.App", Reference(core));

        ReleaseLineageCompiler.ValidatePackageContinuity(
            [new ReleaseLineagePackage(core.PackageId, core.ProjectPath)],
            new PackageGraph([app, core]));
    }

    [Theory]
    [InlineData(".koan-package-lineage.json")]
    [InlineData("src/App/.koan-package-lineage-marker.json")]
    public void SourceCannotOwnReservedLineagePaths(string path)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.ValidateReservedSourcePaths([path]));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    private static string Reference(PackageProject project) =>
        Path.Combine(project.ProjectDirectory, Path.GetFileName(project.ProjectPath));

    private static IReadOnlyDictionary<string, string?> Versions(
        PackageProject first,
        PackageProject second,
        PackageProject third,
        PackageProject? fourth = null,
        IReadOnlyCollection<string>? fresh = null)
    {
        fresh ??= [];
        return new[] { first, second, third }
            .Concat(fourth is null ? [] : [fourth])
            .ToDictionary(
                project => project.PackageId,
                project => (string?)(fresh.Contains(project.PackageId, StringComparer.OrdinalIgnoreCase) ? "0.17.2" : "0.17.1"),
                StringComparer.OrdinalIgnoreCase);
    }

    private static PackageProject Project(string id, params string[] references)
    {
        var name = id.Replace('.', '-');
        var directory = Path.Combine(Path.GetTempPath(), "koan-lineage-tests", name);
        return new PackageProject(
            $"src/{name}/{name}.csproj",
            directory,
            id,
            "Package",
            true,
            "README.md",
            "Description",
            "koan;test",
            references);
    }
}
