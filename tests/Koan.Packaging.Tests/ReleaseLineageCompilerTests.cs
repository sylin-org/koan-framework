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

    [Theory]
    [InlineData("0.17.1")]
    [InlineData("0.17-beta")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    public void VersionIntentMustBeExactlyMajorMinor(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.IsBreakingTierAdvance("0.17", value));

        Assert.Contains("major.minor", error.Message, StringComparison.OrdinalIgnoreCase);
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
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            isBootstrap: false,
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
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            isBootstrap: false,
            Versions(left, right, bridge),
            Versions(left, right, bridge, fresh: [left.PackageId, right.PackageId]));

        var markerId = Assert.Single(plan.MarkerPackages);
        var marker = Assert.Single(plan.Triggers, trigger => trigger.PackageId == markerId);
        Assert.Equal(bridge.PackageId, marker.PackageId);
        Assert.Equal([left.PackageId, right.PackageId], marker.BreakingRoots);
    }

    [Fact]
    public void SharedInputTouchesOnlyItsEvaluatedConsumers()
    {
        const string shared = "src/Connectors/Directory.Build.props";
        var core = Project("Sylin.Koan.Core");
        var connector = Project("Sylin.Koan.Connector") with { SharedInputs = [shared] };
        var app = Project("Sylin.Koan.App", Reference(core));
        var graph = new PackageGraph([connector, app, core]);
        var impact = ReleaseLineageCompiler.MapChangedSharedInputs(graph, [], [shared]);

        var plan = ReleaseLineageCompiler.Plan(
            graph,
            [],
            impact,
            isBootstrap: false,
            Versions(core, connector, app),
            Versions(core, connector, app));

        var marker = Assert.Single(plan.Triggers);
        Assert.Equal(connector.PackageId, marker.PackageId);
        Assert.Equal([shared], marker.SharedInputs);
        Assert.Equal([connector.PackageId], plan.MarkerPackages);
    }

    [Fact]
    public void PreviousAndCurrentInputUnionPreservesDeletionAndRenameOwnership()
    {
        const string deleted = "shared/old.txt";
        const string added = "shared/new.txt";
        var owner = Project("Sylin.Koan.Owner") with { SharedInputs = [added] };
        var unrelated = Project("Sylin.Koan.Unrelated");
        var previous = new ReleaseLineagePackage(owner.PackageId, owner.ProjectPath, "0.20.1")
        {
            SharedInputs = [deleted]
        };

        var impact = ReleaseLineageCompiler.MapChangedSharedInputs(
            new PackageGraph([unrelated, owner]),
            [previous],
            [deleted, added]);

        var inputs = Assert.Single(impact);
        Assert.Equal(owner.PackageId, inputs.Key);
        Assert.Equal([added, deleted], inputs.Value);
    }

    [Fact]
    public void BootstrapTouchesEveryPackageOwnerOnce()
    {
        var core = Project("Sylin.Koan.Core");
        var data = Project("Sylin.Koan.Data", Reference(core));
        var app = Project("Sylin.Koan.App", Reference(data));
        var graph = new PackageGraph([app, data, core]);

        var plan = ReleaseLineageCompiler.Plan(
            graph,
            [],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            isBootstrap: true,
            Versions(core, data, app),
            Versions(core, data, app));

        Assert.Equal([core.PackageId, data.PackageId, app.PackageId], plan.ClosurePackages);
        Assert.Equal(plan.ClosurePackages, plan.MarkerPackages);
    }

    [Fact]
    public void ExistingClosureMemberRequiresDurablePriorIdentity()
    {
        var core = Project("Sylin.Koan.Core");
        var data = Project("Sylin.Koan.Data", Reference(core));
        var app = Project("Sylin.Koan.App", Reference(data));
        var graph = new PackageGraph([app, data, core]);
        var previous = new Dictionary<string, string?>(Versions(core, data, app), StringComparer.OrdinalIgnoreCase)
        {
            [core.PackageId] = null
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.Plan(
                graph,
                [core.PackageId],
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                isBootstrap: false,
                previous,
                Versions(core, data, app)));

        Assert.Contains("durable version identity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviousPackageDeletionBecomesPermanentRetirement()
    {
        var core = Project("Sylin.Koan.Core");
        var graph = new PackageGraph([core]);
        var previous = new[]
        {
            new ReleaseLineagePackage(core.PackageId, core.ProjectPath, "0.17.1"),
            new ReleaseLineagePackage("Sylin.Koan.Removed", "src/removed/removed.csproj", "0.17.1")
        };

        var retired = ReleaseLineageCompiler.ReconcilePackageContinuity(previous, [], graph);

        var package = Assert.Single(retired);
        Assert.Equal("Sylin.Koan.Removed", package.PackageId);
        Assert.Equal("0.17.1", package.Version);
    }

    [Fact]
    public void PreviousPackageRenameFailsLoudly()
    {
        var current = Project("Sylin.Koan.Core");
        var graph = new PackageGraph([current]);
        var previous = new[]
        {
            new ReleaseLineagePackage(current.PackageId, "src/old/Koan.Core.csproj", "0.17.1")
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.ReconcilePackageContinuity(previous, [], graph));

        Assert.Contains("rename", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(current.PackageId, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewPackageIsAllowedByContinuityGuard()
    {
        var core = Project("Sylin.Koan.Core");
        var app = Project("Sylin.Koan.App", Reference(core));

        var retired = ReleaseLineageCompiler.ReconcilePackageContinuity(
            [new ReleaseLineagePackage(core.PackageId, core.ProjectPath, "0.17.1")],
            [],
            new PackageGraph([app, core]));

        Assert.Empty(retired);
    }

    [Fact]
    public void RetiredPackageIdentityCannotBeReintroduced()
    {
        var returned = Project("Sylin.Koan.Removed");
        var retired = new[]
        {
            new ReleaseLineagePackage(returned.PackageId, "src/old/removed.csproj", "0.17.4")
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseLineageCompiler.ReconcilePackageContinuity([], retired, new PackageGraph([returned])));

        Assert.Contains("retired package identity", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(returned.PackageId, error.Message, StringComparison.Ordinal);
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
            false,
            true,
            "README.md",
            "Description",
            "koan;test",
            references,
            []);
    }
}
