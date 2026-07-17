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

    [Fact]
    public async Task Evaluated_package_inputs_assign_sibling_payload_changes_to_the_owning_package()
    {
        using var repository = TestRepository.Create(
            "0.18",
            withExternalPayload: true,
            withPackageInput: true);
        await repository.InitializeGitAsync();
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var owner = Assert.Single(await inspector.DiscoverPackagesAsync(CancellationToken.None));
        var input = Assert.Single(owner.SharedInputs, path =>
            path.EndsWith("build/Generator/Generator.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(owner.SharedInputs, path =>
            path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
        var impact = ReleaseLineageCompiler.MapChangedSharedInputs(
            new PackageGraph([owner]),
            [],
            [input]);

        var changed = Assert.Single(impact);
        Assert.Equal(TestRepository.PackageId, changed.Key);
        Assert.Equal([input], changed.Value);
    }

    [Fact]
    public async Task External_generated_payload_without_source_ownership_fails_correctively()
    {
        using var repository = TestRepository.Create("0.18", withExternalPayload: true);
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(TestRepository.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains(PackagingConstants.PackageInputItemName, error.Message, StringComparison.Ordinal);
        Assert.Contains("fresh owning package version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyzer_project_sources_are_automatic_inputs_of_each_packable_consumer()
    {
        using var repository = TestRepository.Create("0.18", withAnalyzerProject: true);
        await repository.InitializeGitAsync();
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var owner = Assert.Single(await inspector.DiscoverPackagesAsync(CancellationToken.None));
        var input = Assert.Single(owner.SharedInputs, path =>
            path.EndsWith("build/Generator/Generator.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(owner.SharedInputs, path =>
            path.Contains("/artifacts/", StringComparison.OrdinalIgnoreCase));
        var impact = ReleaseLineageCompiler.MapChangedSharedInputs(
            new PackageGraph([owner]),
            [],
            [input]);

        Assert.Equal(TestRepository.PackageId, Assert.Single(impact).Key);
    }

    [Fact]
    public async Task Repeated_inventory_uses_a_fresh_tracked_source_snapshot()
    {
        using var repository = TestRepository.Create("0.18", withAnalyzerProject: true);
        await repository.InitializeGitAsync();
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var first = Assert.Single(await inspector.DiscoverPackagesAsync(CancellationToken.None));
        const string addedInput = "build/Generator/AddedAfterFirstInventory.cs";
        Assert.DoesNotContain(first.SharedInputs, path =>
            path.EndsWith(addedInput, StringComparison.Ordinal));

        await repository.AddTrackedAnalyzerSourceAsync("AddedAfterFirstInventory.cs");
        var second = Assert.Single(await inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(second.SharedInputs, path =>
            path.EndsWith(addedInput, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ignored_package_input_cannot_claim_external_payload_ownership()
    {
        using var repository = TestRepository.Create(
            "0.18",
            withExternalPayload: true,
            withIgnoredPackageInput: true);
        await repository.InitializeGitAsync();
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(PackagingConstants.PackageInputItemName, error.Message, StringComparison.Ordinal);
        Assert.Contains("not tracked by Git", error.Message, StringComparison.Ordinal);
        Assert.Contains("ignored or local artifacts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ignored_external_payload_without_tracked_source_ownership_fails_correctively()
    {
        using var repository = TestRepository.Create(
            "0.18",
            withIgnoredExternalPayload: true);
        await repository.InitializeGitAsync();
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(TestRepository.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains("ignored, or untracked external payload", error.Message, StringComparison.Ordinal);
        Assert.Contains(PackagingConstants.PackageInputItemName, error.Message, StringComparison.Ordinal);
        Assert.Contains("fresh owning package version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Payload_outside_the_versioned_repository_is_rejected()
    {
        using var repository = TestRepository.Create(
            "0.18",
            withOutsideRepositoryPayload: true);
        var inspector = new RepositoryInspector(repository.Root, new ProcessRunner());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inspector.DiscoverPackagesAsync(CancellationToken.None));

        Assert.Contains(TestRepository.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains("outside the versioned repository", error.Message, StringComparison.Ordinal);
        Assert.Contains("reproducible from repository-owned inputs", error.Message, StringComparison.Ordinal);
    }

    private sealed class TestRepository : IDisposable
    {
        public const string PackageId = "Sylin.Koan.Test.VersionIntent";

        private TestRepository(string root) => Root = root;

        public string Root { get; }

        public static TestRepository Create(
            string? version,
            bool withExternalPayload = false,
            bool withPackageInput = false,
            bool withAnalyzerProject = false,
            bool withIgnoredPackageInput = false,
            bool withIgnoredExternalPayload = false,
            bool withOutsideRepositoryPayload = false)
        {
            var root = Path.Combine(FindKoanRoot(), "tmp", "package-version-intent-tests", Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "src", "Example");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project />" + Environment.NewLine);
            var payloadItems = string.Empty;
            if (withExternalPayload ||
                withPackageInput ||
                withAnalyzerProject ||
                withIgnoredPackageInput ||
                withIgnoredExternalPayload ||
                withOutsideRepositoryPayload)
            {
                var generatorDirectory = Path.Combine(root, "build", "Generator");
                Directory.CreateDirectory(generatorDirectory);
                File.WriteAllText(
                    Path.Combine(generatorDirectory, "Generator.cs"),
                    "namespace Generator; public sealed class Marker;" + Environment.NewLine);
                var items = new List<string>();
                if (withAnalyzerProject || withIgnoredPackageInput || withIgnoredExternalPayload)
                {
                    var ignoredArtifacts = Path.Combine(generatorDirectory, "artifacts");
                    Directory.CreateDirectory(ignoredArtifacts);
                    File.WriteAllBytes(Path.Combine(ignoredArtifacts, "local-generator.dll"), [0x00]);
                    File.WriteAllText(
                        Path.Combine(root, ".gitignore"),
                        "build/Generator/artifacts/" + Environment.NewLine);
                }
                if (withAnalyzerProject)
                {
                    File.WriteAllText(
                        Path.Combine(generatorDirectory, "Generator.csproj"),
                        """
                        <Project Sdk="Microsoft.NET.Sdk">
                          <PropertyGroup>
                            <TargetFramework>netstandard2.0</TargetFramework>
                            <IsPackable>false</IsPackable>
                          </PropertyGroup>
                        </Project>
                        """ + Environment.NewLine);
                    items.Add("<ProjectReference Include=\"../../build/Generator/Generator.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />");
                }
                if (withExternalPayload)
                {
                    var output = Path.Combine(generatorDirectory, "bin", "Debug", "net10.0");
                    Directory.CreateDirectory(output);
                    File.WriteAllBytes(Path.Combine(output, "Generator.dll"), [0x4b, 0x6f, 0x61, 0x6e]);
                    items.Add("<None Include=\"../../build/Generator/bin/Debug/net10.0/Generator.dll\" Pack=\"true\" PackagePath=\"build/tools/Generator.dll\" />");
                }
                if (withPackageInput)
                {
                    items.Add("<KoanPackageInput Include=\"../../build/Generator/Generator.cs\" />");
                }
                if (withIgnoredPackageInput)
                {
                    items.Add("<KoanPackageInput Include=\"../../build/Generator/artifacts/local-generator.dll\" />");
                }
                if (withIgnoredExternalPayload)
                {
                    items.Add("<None Include=\"../../build/Generator/artifacts/local-generator.dll\" Pack=\"true\" PackagePath=\"build/tools/Generator.dll\" />");
                }
                if (withOutsideRepositoryPayload)
                {
                    var outsideDirectory = root + "-outside";
                    Directory.CreateDirectory(outsideDirectory);
                    var outsidePayload = Path.Combine(outsideDirectory, "local-generator.dll");
                    File.WriteAllBytes(outsidePayload, [0x00]);
                    items.Add($"<None Include=\"{outsidePayload}\" Pack=\"true\" PackagePath=\"build/tools/Generator.dll\" />");
                }
                payloadItems = $$"""
                  <ItemGroup>
                    {{string.Join(Environment.NewLine + "    ", items)}}
                  </ItemGroup>
                """;
            }
            File.WriteAllText(Path.Combine(projectDirectory, "Example.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsPackable>true</IsPackable>
                    <PackageId>{{PackageId}}</PackageId>
                  </PropertyGroup>
                {{payloadItems}}
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

        public async Task InitializeGitAsync()
        {
            var runner = new ProcessRunner();
            await runner.RequireAsync(
                "git",
                ["init", "--quiet"],
                Root,
                CancellationToken.None);
            await runner.RequireAsync(
                "git",
                ["add", "--all"],
                Root,
                CancellationToken.None);
        }

        public async Task AddTrackedAnalyzerSourceAsync(string fileName)
        {
            var relative = Path.Combine("build", "Generator", fileName);
            File.WriteAllText(
                Path.Combine(Root, relative),
                "namespace Generator; public sealed class AddedAfterFirstInventory;" + Environment.NewLine);
            await new ProcessRunner().RequireAsync(
                "git",
                ["add", "--", relative.Replace('\\', '/')],
                Root,
                CancellationToken.None);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
            try { Directory.Delete(Root + "-outside", recursive: true); } catch { }
        }

        private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
    }
}
