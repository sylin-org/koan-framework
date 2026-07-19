using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleaseLineageGitTests
{
    [Fact]
    public async Task CommittedLineageMaterializationIsExactAndReadOnly()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);
        var headBefore = await fixture.HeadAsync();

        var materialized = await compiler.MaterializeCommittedAsync(
            bootstrap.VersionCommit,
            CancellationToken.None);
        var output = Path.Combine(
            fixture.Root,
            "artifacts",
            "release",
            PackagingConstants.LineageArtifactFileName);
        await ReleaseLineageCompiler.SaveAsync(materialized, output, CancellationToken.None);
        var loaded = await ReleaseLineageCompiler.LoadAsync(output, CancellationToken.None);

        ReleaseLineageCompiler.RequireCommittedMatch(loaded, bootstrap);
        Assert.Equal(bootstrap.VersionCommit, materialized.VersionCommit);
        Assert.Equal(headBefore, await fixture.HeadAsync());
        Assert.Equal(string.Empty, await fixture.TrackedStatusAsync());
    }

    [Fact]
    public async Task CommittedLineageMaterializationRequiresExactCleanCheckout()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);

        await fixture.SwitchAsync("dev");
        var checkoutError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.MaterializeCommittedAsync(bootstrap.VersionCommit, CancellationToken.None));
        Assert.Contains("checkout", checkoutError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bootstrap.VersionCommit, checkoutError.Message, StringComparison.OrdinalIgnoreCase);

        await fixture.SwitchAsync("automation/package-lineage-dev");
        fixture.Touch(LineageRepository.CoreId, "dirty tracked input");
        var dirtyError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.MaterializeCommittedAsync(bootstrap.VersionCommit, CancellationToken.None));
        Assert.Contains("no tracked modifications", dirtyError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommittedLineageMaterializationRejectsWrongSchemaAndTamper()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);
        var original = fixture.ReadLineageState();

        var wrongSchema = JsonNode.Parse(original)!.AsObject();
        wrongSchema["schemaVersion"] = PackagingConstants.ReleaseLineageSchema + 1;
        fixture.WriteLineageState(wrongSchema.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var wrongSchemaCommit = await fixture.AmendAsync();
        var schemaError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.MaterializeCommittedAsync(wrongSchemaCommit, CancellationToken.None));
        Assert.Contains("expected", schemaError.Message, StringComparison.OrdinalIgnoreCase);

        var tampered = JsonNode.Parse(original)!.AsObject();
        tampered["closurePackages"]!.AsArray().RemoveAt(0);
        fixture.WriteLineageState(tampered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var tamperedCommit = await fixture.AmendAsync();
        var tamperError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.MaterializeCommittedAsync(tamperedCommit, CancellationToken.None));
        Assert.Contains("inconsistent closure", tamperError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TwoBreakingWavesMintDistinctClosureAndLeafRemainsIndependent()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);

        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);
        Assert.True(bootstrap.IsBootstrap);
        Assert.Equal(
            [LineageRepository.CoreId, LineageRepository.DataId, LineageRepository.AppId, LineageRepository.UnrelatedId],
            bootstrap.ClosurePackages);
        Assert.Equal(bootstrap.ClosurePackages, bootstrap.MarkerPackages);
        Assert.Equal(fixture.BaseCommit, await fixture.ParentAsync(bootstrap.VersionCommit));

        await fixture.SwitchAsync("dev");
        fixture.WriteVersion(LineageRepository.CoreId, "0.18");
        var breakingSource = await fixture.CommitAsync("break core compatibility tier");
        var first = await compiler.CompileAsync(
            breakingSource,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            bootstrap.VersionCommit,
            CancellationToken.None);

        Assert.Equal(
            [LineageRepository.CoreId, LineageRepository.DataId, LineageRepository.AppId],
            first.ClosurePackages);
        Assert.Equal([LineageRepository.DataId, LineageRepository.AppId], first.MarkerPackages);
        Assert.Empty(first.BreakingRoots.Except([LineageRepository.CoreId], StringComparer.OrdinalIgnoreCase));
        Assert.True(File.Exists(fixture.MarkerPath(LineageRepository.DataId)));
        Assert.True(File.Exists(fixture.MarkerPath(LineageRepository.AppId)));
        Assert.True(File.Exists(fixture.MarkerPath(LineageRepository.CoreId)));
        Assert.True(File.Exists(fixture.MarkerPath(LineageRepository.UnrelatedId)));
        Assert.Equal(bootstrap.VersionCommit, await fixture.ParentAsync(first.VersionCommit));
        Assert.All(
            await fixture.DiffPathsAsync(first.SourceCommit, first.VersionCommit),
            path => Assert.True(
                string.Equals(path, PackagingConstants.LineageStateFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(path), PackagingConstants.LineageMarkerFileName, StringComparison.OrdinalIgnoreCase),
                $"Unexpected lineage projection path: {path}"));

        var bootstrapVersions = Versions(bootstrap);
        var firstVersions = Versions(first);
        foreach (var packageId in first.ClosurePackages)
        {
            Assert.NotEqual(bootstrapVersions[packageId], firstVersions[packageId]);
        }
        Assert.Equal(
            bootstrapVersions[LineageRepository.UnrelatedId],
            firstVersions[LineageRepository.UnrelatedId]);

        using var http = new HttpClient();
        var planner = new ReleasePlanner(repository, new NuGetRegistry(http));
        var firstManifest = await planner.CreateAsync(first, offline: true, CancellationToken.None);
        Assert.Equal(first.ClosurePackages, firstManifest.Packages.Select(package => package.PackageId));
        Assert.All(firstManifest.Packages, package => Assert.NotEqual(package.PreviousVersion, package.Version));
        var tampered = new ReleaseLineage
        {
            PreviousSourceCommit = first.PreviousSourceCommit,
            SourceCommit = first.SourceCommit,
            PreviousVersionCommit = first.PreviousVersionCommit,
            VersionCommit = first.VersionCommit,
            BreakingRoots = first.BreakingRoots.ToList(),
            ClosurePackages = first.ClosurePackages.ToList(),
            MarkerPackages = [],
            Triggers = first.Triggers.ToList()
        };
        var tamperError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            planner.CreateAsync(tampered, offline: true, CancellationToken.None));
        Assert.Contains("committed state", tamperError.Message, StringComparison.OrdinalIgnoreCase);

        var replay = await compiler.CompileAsync(
            breakingSource,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            first.VersionCommit,
            CancellationToken.None);
        Assert.Equal(first.VersionCommit, replay.VersionCommit);
        var replayManifest = await new ReleasePlanner(
                repository,
                new NuGetRegistry(new HttpClient(new AlwaysPublishedHandler())))
            .CreateAsync(replay, offline: false, CancellationToken.None);
        Assert.Equal(first.ClosurePackages, replayManifest.Packages.Select(package => package.PackageId));
        Assert.All(replayManifest.Packages, package => Assert.True(package.AlreadyPublished));

        await fixture.SwitchAsync("dev");
        fixture.Touch(LineageRepository.UnrelatedId, "ordinary leaf patch");
        var leafSource = await fixture.CommitAsync("patch unrelated leaf");
        var second = await compiler.CompileAsync(
            leafSource,
            breakingSource,
            "automation/package-lineage-dev",
            first.VersionCommit,
            CancellationToken.None);

        Assert.Empty(second.BreakingRoots);
        Assert.Empty(second.ClosurePackages);
        Assert.Empty(second.MarkerPackages);
        firstVersions = Versions(first);
        var secondVersions = Versions(second);
        foreach (var packageId in first.ClosurePackages)
        {
            var previous = firstVersions[packageId];
            var current = secondVersions[packageId];
            Assert.True(
                string.Equals(previous, current, StringComparison.OrdinalIgnoreCase),
                $"Ordinary leaf patch advanced unrelated closure package {packageId}: {previous} -> {current}.");
        }
        Assert.NotEqual(
            firstVersions[LineageRepository.UnrelatedId],
            secondVersions[LineageRepository.UnrelatedId]);

        await fixture.SwitchAsync("dev");
        fixture.TouchRootBuild("shared package policy");
        var sharedSource = await fixture.CommitAsync("change shared package policy");
        var shared = await compiler.CompileAsync(
            sharedSource,
            leafSource,
            "automation/package-lineage-dev",
            second.VersionCommit,
            CancellationToken.None);
        Assert.Equal(
            [LineageRepository.CoreId, LineageRepository.DataId, LineageRepository.AppId, LineageRepository.UnrelatedId],
            shared.ClosurePackages);
        Assert.Equal(["Directory.Build.props"], shared.SharedInputs);
        Assert.Equal(shared.ClosurePackages, shared.MarkerPackages);

        await fixture.SwitchAsync("dev");
        fixture.WriteVersion(LineageRepository.CoreId, "0.19");
        var secondBreakingSource = await fixture.CommitAsync("break core compatibility tier again");
        var third = await compiler.CompileAsync(
            secondBreakingSource,
            sharedSource,
            "automation/package-lineage-dev",
            shared.VersionCommit,
            CancellationToken.None);

        Assert.Equal(first.ClosurePackages, third.ClosurePackages);
        Assert.Equal([LineageRepository.DataId, LineageRepository.AppId], third.MarkerPackages);
        var sharedVersions = Versions(shared);
        var thirdVersions = Versions(third);
        foreach (var packageId in third.ClosurePackages)
        {
            Assert.NotEqual(sharedVersions[packageId], thirdVersions[packageId]);
        }
        Assert.Equal(
            sharedVersions[LineageRepository.UnrelatedId],
            thirdVersions[LineageRepository.UnrelatedId]);
    }

    [Fact]
    public async Task ManualLineageCommitIsRejectedBeforeProjection()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);

        fixture.Touch(LineageRepository.UnrelatedId, "manual lineage contamination");
        var contaminated = await fixture.CommitAsync("manual lineage contamination");
        await fixture.SwitchAsync("dev");
        fixture.Touch(LineageRepository.CoreId, "next source event");
        var source = await fixture.CommitAsync("next source event");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.CompileAsync(
                source,
                fixture.BaseCommit,
                "automation/package-lineage-dev",
                contaminated,
                CancellationToken.None));

        Assert.Contains("exactly one parent", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bootstrap.VersionCommit, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BootstrapRejectsNonCanonicalCurrentVersionIntent()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var compiler = new ReleaseLineageCompiler(
            fixture.Root,
            process,
            new RepositoryInspector(fixture.Root, process));
        fixture.WriteVersion(LineageRepository.CoreId, "0.17.1");
        var source = await fixture.CommitAsync("use non-canonical package intent");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.CompileAsync(
                source,
                fixture.BaseCommit,
                "automation/package-lineage-dev",
                previousLineageRevision: null,
                CancellationToken.None));

        Assert.Contains("exactly unsigned major.minor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BootstrapRecordsSourceRangeDeletionAsRetired()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        fixture.RemoveProject(LineageRepository.UnrelatedId);
        var source = await fixture.CommitAsync("retire package before first lineage projection");
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);

        var lineage = await compiler.CompileAsync(
            source,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);

        Assert.True(lineage.IsBootstrap);
        var retired = Assert.Single(lineage.RetiredPackages);
        Assert.Equal(LineageRepository.UnrelatedId, retired.PackageId);
        Assert.False(string.IsNullOrWhiteSpace(retired.Version));
        Assert.DoesNotContain(lineage.ClosurePackages, id => id == LineageRepository.UnrelatedId);

        var manifest = await new ReleasePlanner(repository, new NuGetRegistry(new HttpClient()))
            .CreateAsync(lineage, offline: true, CancellationToken.None);
        Assert.DoesNotContain(manifest.Packages, package => package.PackageId == LineageRepository.UnrelatedId);
    }

    [Fact]
    public async Task PackageDeletionRetiresIdentityWithoutPublishingAnArtifact()
    {
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);
        var finalIdentity = Versions(bootstrap)[LineageRepository.UnrelatedId];

        await fixture.SwitchAsync("dev");
        fixture.RemoveProject(LineageRepository.UnrelatedId);
        var retirementSource = await fixture.CommitAsync("retire unrelated package");
        var retirement = await compiler.CompileAsync(
            retirementSource,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            bootstrap.VersionCommit,
            CancellationToken.None);

        var retired = Assert.Single(retirement.RetiredPackages);
        Assert.Equal(LineageRepository.UnrelatedId, retired.PackageId);
        Assert.Equal(finalIdentity, retired.Version);
        Assert.DoesNotContain(retirement.Packages, package => package.PackageId == LineageRepository.UnrelatedId);
        Assert.Empty(retirement.ClosurePackages);

        var manifest = await new ReleasePlanner(repository, new NuGetRegistry(new HttpClient()))
            .CreateAsync(retirement, offline: true, CancellationToken.None);
        Assert.Empty(manifest.Packages);

        await fixture.SwitchAsync("dev");
        fixture.Touch(LineageRepository.CoreId, "ordinary source after retirement");
        var nextSource = await fixture.CommitAsync("advance after retirement");
        var next = await compiler.CompileAsync(
            nextSource,
            retirementSource,
            "automation/package-lineage-dev",
            retirement.VersionCommit,
            CancellationToken.None);

        var retained = Assert.Single(next.RetiredPackages);
        Assert.Equal(retired.PackageId, retained.PackageId);
        Assert.Equal(retired.ProjectPath, retained.ProjectPath);
        Assert.Equal(retired.Version, retained.Version);
        Assert.Equal(retired.SharedInputs, retained.SharedInputs);
    }

    [Fact]
    public async Task GenuinelyNewPackageMayHaveNoPreviousIdentity()
    {
        const string newPackageId = "Sylin.Koan.Test.New";
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);

        await fixture.SwitchAsync("dev");
        fixture.AddProject("New", newPackageId);
        var source = await fixture.CommitAsync("add package owner");
        var lineage = await compiler.CompileAsync(
            source,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            bootstrap.VersionCommit,
            CancellationToken.None);
        var manifest = await new ReleasePlanner(repository, new NuGetRegistry(new HttpClient()))
            .CreateAsync(lineage, offline: true, CancellationToken.None);

        var package = Assert.Single(manifest.Packages, item => item.PackageId == newPackageId);
        Assert.Null(package.PreviousVersion);
        Assert.Empty(lineage.ClosurePackages);
    }

    [Fact]
    public async Task DistinctRetirementAndNewOwnerIgnoreGitFileSimilarity()
    {
        const string newPackageId = "Sylin.Koan.Test.Replacement";
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);
        var retiredIdentity = Versions(bootstrap)[LineageRepository.UnrelatedId];

        await fixture.SwitchAsync("dev");
        fixture.RemoveProject(LineageRepository.UnrelatedId);
        fixture.AddProject("Replacement", newPackageId);
        var source = await fixture.CommitAsync("retire one owner and add a distinct package");
        var lineage = await compiler.CompileAsync(
            source,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            bootstrap.VersionCommit,
            CancellationToken.None);
        var manifest = await new ReleasePlanner(repository, new NuGetRegistry(new HttpClient()))
            .CreateAsync(lineage, offline: true, CancellationToken.None);

        var retired = Assert.Single(lineage.RetiredPackages);
        Assert.Equal(LineageRepository.UnrelatedId, retired.PackageId);
        Assert.Equal(retiredIdentity, retired.Version);
        Assert.Contains(lineage.Packages, package => package.PackageId == newPackageId);
        var added = Assert.Single(manifest.Packages, package => package.PackageId == newPackageId);
        Assert.Null(added.PreviousVersion);
        Assert.DoesNotContain(manifest.Packages, package => package.PackageId == LineageRepository.UnrelatedId);
    }

    [Fact]
    public async Task EvaluatedExternalInputHistorySelectsOnlyItsOwnerAcrossEveryMutation()
    {
        const string firstInput = "shared/catalog-a.txt";
        const string renamedInput = "shared/catalog-b.txt";
        await using var fixture = await LineageRepository.CreateAsync();
        var process = new ProcessRunner();
        var repository = new RepositoryInspector(fixture.Root, process);
        var compiler = new ReleaseLineageCompiler(fixture.Root, process, repository);
        var planner = new ReleasePlanner(repository, new NuGetRegistry(new HttpClient()));
        var bootstrap = await compiler.CompileAsync(
            fixture.BaseCommit,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);

        await fixture.SwitchAsync("dev");
        fixture.ConfigureExternalPackInputs(LineageRepository.UnrelatedId);
        fixture.WriteSharedInput("catalog-a.txt", "added");
        var addSource = await fixture.CommitAsync("add evaluated external package input");
        var added = await compiler.CompileAsync(
            addSource,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            bootstrap.VersionCommit,
            CancellationToken.None);
        await AssertInputWaveAsync(bootstrap, added, [firstInput]);
        Assert.Contains(
            firstInput,
            added.Packages.Single(package => package.PackageId == LineageRepository.UnrelatedId).SharedInputs);

        await fixture.SwitchAsync("dev");
        fixture.WriteSharedInput("catalog-a.txt", "changed");
        var changeSource = await fixture.CommitAsync("change evaluated external package input");
        var changed = await compiler.CompileAsync(
            changeSource,
            addSource,
            "automation/package-lineage-dev",
            added.VersionCommit,
            CancellationToken.None);
        await AssertInputWaveAsync(added, changed, [firstInput]);

        await fixture.SwitchAsync("dev");
        fixture.RenameSharedInput("catalog-a.txt", "catalog-b.txt");
        var renameSource = await fixture.CommitAsync("rename evaluated external package input");
        var renamed = await compiler.CompileAsync(
            renameSource,
            changeSource,
            "automation/package-lineage-dev",
            changed.VersionCommit,
            CancellationToken.None);
        await AssertInputWaveAsync(changed, renamed, [firstInput, renamedInput]);
        var renamedMap = renamed.Packages
            .Single(package => package.PackageId == LineageRepository.UnrelatedId)
            .SharedInputs;
        Assert.DoesNotContain(firstInput, renamedMap);
        Assert.Contains(renamedInput, renamedMap);

        await fixture.SwitchAsync("dev");
        fixture.DeleteSharedInput("catalog-b.txt");
        var deleteSource = await fixture.CommitAsync("delete evaluated external package input");
        var deleted = await compiler.CompileAsync(
            deleteSource,
            renameSource,
            "automation/package-lineage-dev",
            renamed.VersionCommit,
            CancellationToken.None);
        await AssertInputWaveAsync(renamed, deleted, [renamedInput]);
        Assert.DoesNotContain(
            renamedInput,
            deleted.Packages.Single(package => package.PackageId == LineageRepository.UnrelatedId).SharedInputs);

        async Task AssertInputWaveAsync(
            ReleaseLineage previous,
            ReleaseLineage current,
            IReadOnlyList<string> expectedInputs)
        {
            Assert.Equal([LineageRepository.UnrelatedId], current.ClosurePackages);
            var trigger = Assert.Single(current.Triggers);
            Assert.Equal(LineageRepository.UnrelatedId, trigger.PackageId);
            Assert.Equal(expectedInputs, trigger.SharedInputs);

            var previousVersions = Versions(previous);
            var currentVersions = Versions(current);
            Assert.NotEqual(
                previousVersions[LineageRepository.UnrelatedId],
                currentVersions[LineageRepository.UnrelatedId]);
            foreach (var packageId in currentVersions.Keys.Where(id => id != LineageRepository.UnrelatedId))
            {
                Assert.Equal(previousVersions[packageId], currentVersions[packageId]);
            }

            var manifest = await planner.CreateAsync(current, offline: true, CancellationToken.None);
            Assert.Equal(
                [LineageRepository.UnrelatedId],
                manifest.Packages.Select(package => package.PackageId));
        }
    }

    private static IReadOnlyDictionary<string, string?> Versions(ReleaseLineage lineage) =>
        lineage.Packages.ToDictionary(
            package => package.PackageId,
            package => package.Version,
            StringComparer.OrdinalIgnoreCase);

    private sealed class AlwaysPublishedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private sealed class LineageRepository : IAsyncDisposable
    {
        public const string CoreId = "Sylin.Koan.Test.Core";
        public const string DataId = "Sylin.Koan.Test.Data";
        public const string AppId = "Sylin.Koan.Test.App";
        public const string UnrelatedId = "Sylin.Koan.Test.Unrelated";

        private readonly ProcessRunner process = new();
        private readonly Dictionary<string, string> directories;

        private LineageRepository(string root, Dictionary<string, string> directories, string baseCommit)
        {
            Root = root;
            this.directories = directories;
            BaseCommit = baseCommit;
        }

        public string Root { get; }
        public string BaseCommit { get; }

        public static async Task<LineageRepository> CreateAsync()
        {
            var repositoryRoot = FindKoanRoot();
            var root = Path.Combine(repositoryRoot, "tmp", "package-lineage-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>" +
                Environment.NewLine);
            var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CoreId] = CreateProject(root, "Core", CoreId),
                [DataId] = CreateProject(root, "Data", DataId, "../Core/Core.csproj"),
                [AppId] = CreateProject(root, "App", AppId, "../Data/Data.csproj"),
                [UnrelatedId] = CreateProject(root, "Unrelated", UnrelatedId)
            };
            var fixture = new LineageRepository(root, directories, string.Empty);
            await fixture.GitAsync("init", "--initial-branch=dev");
            await fixture.GitAsync("config", "user.name", "Koan Packaging Tests");
            await fixture.GitAsync("config", "user.email", "packaging-tests@koan.invalid");
            await fixture.GitAsync("add", ".");
            await fixture.GitAsync("commit", "--no-gpg-sign", "-m", "initial package graph");
            var baseCommit = await fixture.GitAsync("rev-parse", "HEAD");
            return new LineageRepository(root, directories, baseCommit);
        }

        public void WriteVersion(string packageId, string version) =>
            File.WriteAllText(
                Path.Combine(directories[packageId], "version.json"),
                VersionJson(version) + Environment.NewLine);

        public void Touch(string packageId, string value) =>
            File.AppendAllText(Path.Combine(directories[packageId], "Code.cs"), $"// {value}{Environment.NewLine}");

        public void TouchRootBuild(string value) =>
            File.AppendAllText(Path.Combine(Root, "Directory.Build.props"), $"<!-- {value} -->{Environment.NewLine}");

        public void ConfigureExternalPackInputs(string packageId)
        {
            var project = Directory.GetFiles(directories[packageId], "*.csproj").Single();
            var content = File.ReadAllText(project);
            content = content.Replace(
                "</Project>",
                """
                  <ItemGroup>
                    <None Include="../../shared/*.txt" Pack="true" PackagePath="content/" />
                  </ItemGroup>
                </Project>
                """,
                StringComparison.Ordinal);
            File.WriteAllText(project, content);
        }

        public void WriteSharedInput(string fileName, string value)
        {
            var directory = Path.Combine(Root, "shared");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), value + Environment.NewLine);
        }

        public void RenameSharedInput(string oldName, string newName) =>
            File.Move(Path.Combine(Root, "shared", oldName), Path.Combine(Root, "shared", newName));

        public void DeleteSharedInput(string fileName) =>
            File.Delete(Path.Combine(Root, "shared", fileName));

        public void AddProject(string name, string packageId) =>
            directories[packageId] = CreateProject(Root, name, packageId);

        public void RemoveProject(string packageId)
        {
            Directory.Delete(directories[packageId], recursive: true);
            directories.Remove(packageId);
        }

        public string MarkerPath(string packageId) =>
            Path.Combine(directories[packageId], PackagingConstants.LineageMarkerFileName);

        public async Task<string> CommitAsync(string message)
        {
            await GitAsync("add", ".");
            await GitAsync("commit", "--no-gpg-sign", "-m", message);
            return await GitAsync("rev-parse", "HEAD");
        }

        public Task<string> SwitchAsync(string branch) => GitAsync("switch", branch);

        public Task<string> ParentAsync(string commit) => GitAsync("rev-parse", $"{commit}^");

        public Task<string> HeadAsync() => GitAsync("rev-parse", "HEAD");

        public Task<string> TrackedStatusAsync() =>
            GitAsync("status", "--porcelain", "--untracked-files=no");

        public string ReadLineageState() =>
            File.ReadAllText(Path.Combine(Root, PackagingConstants.LineageStateFileName));

        public void WriteLineageState(string content) =>
            File.WriteAllText(
                Path.Combine(Root, PackagingConstants.LineageStateFileName),
                content + Environment.NewLine);

        public async Task<string> AmendAsync()
        {
            await GitAsync("add", "--", PackagingConstants.LineageStateFileName);
            await GitAsync("commit", "--amend", "--no-gpg-sign", "--no-edit");
            return await HeadAsync();
        }

        public async Task<IReadOnlyList<string>> DiffPathsAsync(string left, string right)
        {
            var output = await GitAsync("diff", "--name-only", left, right, "--");
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }

        private Task<string> GitAsync(params string[] arguments) =>
            process.RequireAsync("git", arguments, Root, CancellationToken.None);

        private static string CreateProject(string root, string name, string packageId, string? reference = null)
        {
            var directory = Path.Combine(root, "src", name);
            Directory.CreateDirectory(directory);
            var referenceItem = reference is null
                ? string.Empty
                : $"<ItemGroup><ProjectReference Include=\"{reference}\" /></ItemGroup>";
            File.WriteAllText(Path.Combine(directory, $"{name}.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsPackable>true</IsPackable>
                    <PackageId>{{packageId}}</PackageId>
                    <Description>Package-lineage acceptance fixture.</Description>
                    <PackageTags>koan;test</PackageTags>
                  </PropertyGroup>
                  {{referenceItem}}
                </Project>
                """ + Environment.NewLine);
            File.WriteAllText(Path.Combine(directory, "version.json"), VersionJson("0.17") + Environment.NewLine);
            File.WriteAllText(Path.Combine(directory, "Code.cs"), $"namespace {name}; public sealed class Marker {{ }}{Environment.NewLine}");
            return directory;
        }

        private static string VersionJson(string version) => $$"""
            {
              "version": "{{version}}",
              "versionHeightOffset": -1,
              "pathFilters": ["."]
            }
            """;

        private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
    }
}
