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

        fixture.WriteVersion(LineageRepository.CoreId, "0.18");
        var breakingSource = await fixture.CommitAsync("break core compatibility tier");
        var first = await compiler.CompileAsync(
            breakingSource,
            fixture.BaseCommit,
            "automation/package-lineage-dev",
            previousLineageRevision: null,
            CancellationToken.None);

        Assert.Equal(
            [LineageRepository.CoreId, LineageRepository.DataId, LineageRepository.AppId],
            first.ClosurePackages);
        Assert.Equal([LineageRepository.DataId, LineageRepository.AppId], first.MarkerPackages);
        Assert.Empty(first.BreakingRoots.Except([LineageRepository.CoreId], StringComparer.OrdinalIgnoreCase));
        Assert.True(File.Exists(fixture.MarkerPath(LineageRepository.DataId)));
        Assert.True(File.Exists(fixture.MarkerPath(LineageRepository.AppId)));
        Assert.False(File.Exists(fixture.MarkerPath(LineageRepository.CoreId)));
        Assert.False(File.Exists(fixture.MarkerPath(LineageRepository.UnrelatedId)));

        var firstProjects = (await repository.DiscoverPackagesAsync(CancellationToken.None))
            .ToDictionary(project => project.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in first.ClosurePackages)
        {
            var previous = await repository.TryGetVersionAsync(firstProjects[packageId], fixture.BaseCommit, CancellationToken.None);
            var current = await repository.TryGetVersionAsync(firstProjects[packageId], first.VersionCommit, CancellationToken.None);
            Assert.NotEqual(previous, current);
        }
        Assert.Equal(
            await repository.TryGetVersionAsync(firstProjects[LineageRepository.UnrelatedId], fixture.BaseCommit, CancellationToken.None),
            await repository.TryGetVersionAsync(firstProjects[LineageRepository.UnrelatedId], first.VersionCommit, CancellationToken.None));

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
        var secondProjects = (await repository.DiscoverPackagesAsync(CancellationToken.None))
            .ToDictionary(project => project.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in first.ClosurePackages)
        {
            var previous = await repository.TryGetVersionAsync(
                secondProjects[packageId],
                first.VersionCommit,
                CancellationToken.None);
            var current = await repository.TryGetVersionAsync(
                secondProjects[packageId],
                second.VersionCommit,
                CancellationToken.None);
            Assert.True(
                string.Equals(previous, current, StringComparison.OrdinalIgnoreCase),
                $"Ordinary leaf patch advanced unrelated closure package {packageId}: {previous} -> {current}.");
        }
        Assert.NotEqual(
            await repository.TryGetVersionAsync(secondProjects[LineageRepository.UnrelatedId], first.VersionCommit, CancellationToken.None),
            await repository.TryGetVersionAsync(secondProjects[LineageRepository.UnrelatedId], second.VersionCommit, CancellationToken.None));

        await fixture.SwitchAsync("dev");
        fixture.WriteVersion(LineageRepository.CoreId, "0.19");
        var secondBreakingSource = await fixture.CommitAsync("break core compatibility tier again");
        var third = await compiler.CompileAsync(
            secondBreakingSource,
            leafSource,
            "automation/package-lineage-dev",
            second.VersionCommit,
            CancellationToken.None);

        Assert.Equal(first.ClosurePackages, third.ClosurePackages);
        Assert.Equal([LineageRepository.DataId, LineageRepository.AppId], third.MarkerPackages);
        var thirdProjects = (await repository.DiscoverPackagesAsync(CancellationToken.None))
            .ToDictionary(project => project.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in third.ClosurePackages)
        {
            Assert.NotEqual(
                await repository.TryGetVersionAsync(thirdProjects[packageId], second.VersionCommit, CancellationToken.None),
                await repository.TryGetVersionAsync(thirdProjects[packageId], third.VersionCommit, CancellationToken.None));
        }
        Assert.Equal(
            await repository.TryGetVersionAsync(thirdProjects[LineageRepository.UnrelatedId], second.VersionCommit, CancellationToken.None),
            await repository.TryGetVersionAsync(thirdProjects[LineageRepository.UnrelatedId], third.VersionCommit, CancellationToken.None));
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

        public string MarkerPath(string packageId) =>
            Path.Combine(directories[packageId], PackagingConstants.LineageMarkerFileName);

        public async Task<string> CommitAsync(string message)
        {
            await GitAsync("add", ".");
            await GitAsync("commit", "--no-gpg-sign", "-m", message);
            return await GitAsync("rev-parse", "HEAD");
        }

        public Task<string> SwitchAsync(string branch) => GitAsync("switch", branch);

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
