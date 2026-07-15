using System.Runtime.CompilerServices;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleaseLineageGitTests
{
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
    public async Task BootstrapAnchorsWithoutRecalculatingHistoricalIdentities()
    {
        await using var fixture = await LineageRepository.CreateAsync(malformedCoreVersion: true);
        var process = new ProcessRunner();
        var compiler = new ReleaseLineageCompiler(
            fixture.Root,
            process,
            new RepositoryInspector(fixture.Root, process));
        fixture.WriteVersion(LineageRepository.CoreId, "0.18");
        var source = await fixture.CommitAsync("repair malformed package version");

        var lineage = await compiler.CompileAsync(
            source,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);

        Assert.True(lineage.IsBootstrap);
        Assert.Equal(lineage.Packages.Count, lineage.ClosurePackages.Count);
        Assert.All(lineage.Packages, package => Assert.False(string.IsNullOrWhiteSpace(package.Version)));
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

    private static IReadOnlyDictionary<string, string?> Versions(ReleaseLineage lineage) =>
        lineage.Packages.ToDictionary(
            package => package.PackageId,
            package => package.Version,
            StringComparer.OrdinalIgnoreCase);

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

        public static async Task<LineageRepository> CreateAsync(bool malformedCoreVersion = false)
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
            if (malformedCoreVersion)
            {
                File.WriteAllText(
                    Path.Combine(directories[CoreId], "version.json"),
                    "{ \"version\": \"not-a-version\", \"pathFilters\": [\".\"] }" + Environment.NewLine);
            }
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

        public void AddProject(string name, string packageId) =>
            directories[packageId] = CreateProject(Root, name, packageId);

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
