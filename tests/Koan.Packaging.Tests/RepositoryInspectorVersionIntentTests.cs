using System.Runtime.CompilerServices;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class RepositoryInspectorVersionIntentTests
{
    [Theory]
    [InlineData("0.18", 0, 18)]
    [InlineData("1.0", 1, 0)]
    [InlineData("12.34", 12, 34)]
    public void CanonicalMajorMinorIntentIsAccepted(string value, int major, int minor)
    {
        var intent = VersionIntent.Parse(value);

        Assert.Equal(major, intent.Major);
        Assert.Equal(minor, intent.Minor);
    }

    [Theory]
    [InlineData("0.18.1")]
    [InlineData("0.18-beta")]
    [InlineData("1")]
    [InlineData(" 0.18")]
    [InlineData("00.18")]
    [InlineData("0.018")]
    [InlineData("999999999999.1")]
    [InlineData("")]
    public void NonCanonicalIntentIsRejected(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => VersionIntent.Parse(value));

        Assert.Contains("exactly unsigned major.minor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\": 18}")]
    [InlineData("[]")]
    public void IntentJsonRequiresStringVersionProperty(string json)
    {
        var error = Assert.Throws<InvalidOperationException>(() => VersionIntent.ParseJson(json));

        Assert.Contains("string 'version' property", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InventoryNamesOwnerPathAndCorrectionForInvalidIntent()
    {
        using var repository = TestRepository.Create("0.18.1");
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(TestRepository.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains("src/Example/version.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("exactly unsigned major.minor", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NBGV owns patch versions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InventoryNamesOwnerPathAndCorrectionForMissingIntent()
    {
        using var repository = TestRepository.Create(version: null);
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(TestRepository.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains("src/Example/version.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("Add 'src/Example/version.json'", error.Message, StringComparison.Ordinal);
        Assert.Contains("NBGV owns patch versions", error.Message, StringComparison.Ordinal);
    }

    private sealed class TestRepository : IDisposable
    {
        public const string PackageId = "Sylin.Koan.Test.VersionIntent";

        private TestRepository(string root) => Root = root;

        public string Root { get; }

        public static TestRepository Create(string? version)
        {
            var root = Path.Combine(FindKoanRoot(), "tmp", "package-version-intent-tests", Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "src", "Example");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "Example.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsPackable>true</IsPackable>
                    <PackageId>{{PackageId}}</PackageId>
                  </PropertyGroup>
                </Project>
                """ + Environment.NewLine);
            if (version is not null)
            {
                File.WriteAllText(Path.Combine(projectDirectory, VersionIntent.FileName), $$"""
                    {
                      "version": "{{version}}",
                      "pathFilters": ["."]
                    }
                    """ + Environment.NewLine);
            }
            return new TestRepository(root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
    }
}
