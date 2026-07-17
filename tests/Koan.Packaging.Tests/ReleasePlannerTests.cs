using System.Net;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleasePlannerTests
{
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

    [Fact]
    public async Task SelectedDependencyMustUseTheSelectedIdentityAsItsFloor()
    {
        var manifest = Manifest(
            Package("Sylin.Koan.Core", "0.18.0"),
            Package(
                "Sylin.Koan.App",
                "0.18.2",
                projectDependencies: ["Sylin.Koan.Core"],
                packageDependencies: [new PackageDependency("Sylin.Koan.Core", "[0.17.5, 0.18.0)", "0.17.5")]));
        var pipeline = Pipeline(HttpStatusCode.OK);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.VerifyClosureAsync(manifest, CancellationToken.None));

        Assert.Contains("release set contains", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false, "missing")]
    [InlineData(false, true, "unexpected")]
    public async Task PackedDependenciesMustMatchTheEvaluatedProjectGraph(
        bool expectedProjectDependency,
        bool actualPackageDependency,
        string expectedMessage)
    {
        var manifest = Manifest(
            Package("Sylin.Koan.Core", "0.18.0"),
            Package(
                "Sylin.Koan.App",
                "0.18.2",
                projectDependencies: expectedProjectDependency ? ["Sylin.Koan.Core"] : [],
                packageDependencies: actualPackageDependency
                    ? [new PackageDependency("Sylin.Koan.Core", "[0.18.0, 0.19.0)", "0.18.0")]
                    : []));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Pipeline(HttpStatusCode.OK).VerifyClosureAsync(manifest, CancellationToken.None));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactSelectedDependencyFloorPassesClosureProof()
    {
        var manifest = Manifest(
            Package("Sylin.Koan.Core", "0.18.0"),
            Package(
                "Sylin.Koan.App",
                "0.18.2",
                projectDependencies: ["Sylin.Koan.Core"],
                packageDependencies: [new PackageDependency("Sylin.Koan.Core", "[0.18.0, 0.19.0)", "0.18.0")]));

        await Pipeline(HttpStatusCode.NotFound).VerifyClosureAsync(manifest, CancellationToken.None);
    }

    [Fact]
    public async Task ManifestWithoutSchemaIsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, $$"""
                {
                  "previousVersionCommit": "{{new string('a', 40)}}",
                  "sourceCommit": "{{new string('b', 40)}}",
                  "versionCommit": "{{new string('c', 40)}}",
                  "createdAtUtc": "1970-01-01T00:00:00Z",
                  "packages": []
                }
                """);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                PackagePipeline.LoadManifestAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LineageArtifactWithoutSchemaIsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, $$"""
                {
                  "previousSourceCommit": "{{new string('a', 40)}}",
                  "sourceCommit": "{{new string('b', 40)}}",
                  "previousVersionCommit": "{{new string('c', 40)}}",
                  "versionCommit": "{{new string('d', 40)}}"
                }
                """);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                ReleaseLineageCompiler.LoadAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LineagePackageWithoutEvaluatedInputMapIsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, $$"""
                {
                  "schemaVersion": {{PackagingConstants.ReleaseLineageSchema}},
                  "previousSourceCommit": "{{new string('a', 40)}}",
                  "sourceCommit": "{{new string('b', 40)}}",
                  "previousVersionCommit": "{{new string('c', 40)}}",
                  "versionCommit": "{{new string('d', 40)}}",
                  "isBootstrap": false,
                  "sharedInputs": [],
                  "packages": [
                    {
                      "packageId": "Sylin.Koan.Core",
                      "projectPath": "src/Koan.Core/Koan.Core.csproj",
                      "version": "0.20.1"
                    }
                  ]
                }
                """);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                ReleaseLineageCompiler.LoadAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PackagePipeline Pipeline(HttpStatusCode registryStatus) =>
        new(
            Path.GetTempPath(),
            new ProcessRunner(),
            new NuGetRegistry(new HttpClient(new StatusHandler(registryStatus))));

    private static ReleaseManifest Manifest(params ReleasePackage[] packages) => new()
    {
        PreviousVersionCommit = new string('a', 40),
        SourceCommit = new string('b', 40),
        VersionCommit = new string('c', 40),
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        Packages = packages.ToList()
    };

    private static ReleasePackage Package(
        string id,
        string version,
        IReadOnlyList<string>? projectDependencies = null,
        IReadOnlyList<PackageDependency>? packageDependencies = null) => new()
    {
        PackageId = id,
        Version = version,
        ProjectPath = $"src/{id}/{id}.csproj",
        Reason = PackagingConstants.VersionChangedReason,
        IncludeSymbols = true,
        ProjectDependencies = projectDependencies?.ToList() ?? [],
        PackageDependencies = packageDependencies?.ToList() ?? []
    };

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

}
