using System.Runtime.CompilerServices;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class RepositoryInspectorVersionOwnershipTests
{
    [Theory]
    [InlineData("1.0", 1, 0)]
    [InlineData("12.34", 12, 34)]
    public void CanonicalReleaseTrainIsAccepted(string value, int major, int minor)
    {
        var train = ReleaseTrain.Parse(value);

        Assert.Equal(major, train.Major);
        Assert.Equal(minor, train.Minor);
    }

    [Theory]
    [InlineData("1.0.1")]
    [InlineData("1.0-beta")]
    [InlineData("1")]
    [InlineData(" 1.0")]
    [InlineData("01.0")]
    [InlineData("")]
    public void NonCanonicalReleaseTrainIsRejected(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReleaseTrain.Parse(value));

        Assert.Contains("exactly unsigned major.minor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InventoryAcceptsAProjectOwnedVersion()
    {
        using var repository = TestRepository.Create(rootVersion: "1.0", projectVersion: "1.0");
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var package = Assert.Single(await inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Equal(TestRepository.PackageId, package.PackageId);
    }

    [Fact]
    public async Task InventoryRejectsAProjectWithoutItsOwnVersionOwner()
    {
        // Inheriting an ancestor version.json would tie the package's patch number to commits that
        // never touched it, which is exactly what per-project ownership exists to prevent.
        using var repository = TestRepository.Create(rootVersion: "1.0", projectVersion: null);
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains("src/Example/version.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("owns its own version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InventoryRequiresAnAncestorVersionOwner()
    {
        using var repository = TestRepository.Create(rootVersion: null, projectVersion: null);
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(TestRepository.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains("repository root", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version owner", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryRootDiscoveryAcceptsAWorktreeGitFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "koan-root-discovery-tests", Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "src", "Example");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: elsewhere");

        try
        {
            Assert.Equal(root, global::PackagingProgram.FindRepositoryRoot(child));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestRepository : IDisposable
    {
        public const string PackageId = "Sylin.Koan.Test.VersionOwnership";

        private TestRepository(string root) => Root = root;

        public string Root { get; }

        public static TestRepository Create(string? rootVersion, string? projectVersion)
        {
            var root = Path.Combine(FindKoanRoot(), "tmp", "package-version-owner-tests", Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "src", "Example");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project />" + Environment.NewLine);
            File.WriteAllText(Path.Combine(projectDirectory, "Example.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsPackable>true</IsPackable>
                    <PackageId>{{PackageId}}</PackageId>
                  </PropertyGroup>
                </Project>
                """ + Environment.NewLine);
            WriteVersion(root, rootVersion);
            WriteVersion(projectDirectory, projectVersion);

            return new TestRepository(root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static void WriteVersion(string directory, string? version)
        {
            if (version is null) return;
            File.WriteAllText(Path.Combine(directory, ReleaseTrain.FileName), $$"""
                {
                  "version": "{{version}}",
                  "pathFilters": ["."]
                }
                """ + Environment.NewLine);
        }

        private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
    }
}
