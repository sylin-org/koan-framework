using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class TemplatePackageCompilerTests
{
    [Fact]
    public void TemplatePackageDeclaresImpactWithoutShippingDependencies()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "templates", "Sylin.Koan.Templates.csproj"));
        var properties = project.Descendants("PropertyGroup").Elements().ToDictionary(
            element => element.Name.LocalName,
            element => element.Value,
            StringComparer.OrdinalIgnoreCase);
        var references = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal("true", properties["SuppressDependenciesWhenPacking"], ignoreCase: true);
        Assert.Equal(3, references.Length);
        Assert.Contains(references, value => value.Contains("Sylin.Koan.csproj", StringComparison.Ordinal));
        Assert.Contains(references, value => value.Contains("Sylin.Koan.App.csproj", StringComparison.Ordinal));
        Assert.Contains(references, value => value.Contains("Koan.Data.Connector.Sqlite.csproj", StringComparison.Ordinal));
        Assert.Equal(
            "GenerateNuspec",
            project.Descendants("Target").Single(element =>
                    element.Attribute("Name")?.Value == "KoanRequirePreparedTemplateContent")
                .Attribute("BeforeTargets")?.Value);
    }

    [Theory]
    [InlineData("0.18.7", "[0.18.7, 0.19.0)")]
    [InlineData("1.4.12", "[1.4.12, 2.0.0)")]
    public void CompilesCanonicalCompatibilityBands(string version, string expected)
    {
        var compatibility = PackageCompatibility.FromVersion(version);

        Assert.Equal(expected, compatibility.Range);
        Assert.True(PackageCompatibility.TryParseRange(expected, out var parsed));
        Assert.Equal(compatibility, parsed);
    }

    [Theory]
    [InlineData("0.18")]
    [InlineData("0.18.7-preview.1")]
    [InlineData("banana")]
    public void RejectsVersionsThatCannotOwnAStableBand(string version)
    {
        var error = Assert.Throws<InvalidOperationException>(() => PackageCompatibility.FromVersion(version));

        Assert.Contains("major.minor.patch", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaterializesBothTemplatesWithoutVersionInputOrTokens()
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PackagingConstants.TemplatePackage.FoundationPackageId] = "0.18.7",
            [PackagingConstants.TemplatePackage.AppPackageId] = "0.17.11",
            [PackagingConstants.TemplatePackage.SqlitePackageId] = "0.17.23"
        };

        using var prepared = await new TemplatePackageCompiler(RepositoryRoot()).PrepareAsync(
            versions,
            CancellationToken.None);

        var web = await File.ReadAllTextAsync(Path.Combine(prepared.Root, "koan-web", "KoanWebApp.csproj"));
        var console = await File.ReadAllTextAsync(Path.Combine(prepared.Root, "koan-console", "KoanConsoleApp.csproj"));
        Assert.Contains("[0.17.11, 0.18.0)", web, StringComparison.Ordinal);
        Assert.Contains("[0.17.23, 0.18.0)", web, StringComparison.Ordinal);
        Assert.Contains("[0.18.7, 0.19.0)", console, StringComparison.Ordinal);
        Assert.Contains("[0.17.23, 0.18.0)", console, StringComparison.Ordinal);
        Assert.DoesNotContain("__KOAN_", web, StringComparison.Ordinal);
        Assert.DoesNotContain("__KOAN_", console, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateDirectories(prepared.Root, "obj", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(prepared.Root, "bin", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RejectsMissingPackageFloorsCorrectively()
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PackagingConstants.TemplatePackage.FoundationPackageId] = "0.18.7",
            [PackagingConstants.TemplatePackage.AppPackageId] = "0.17.11"
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TemplatePackageCompiler(RepositoryRoot()).PrepareAsync(versions, CancellationToken.None));

        Assert.Contains(PackagingConstants.TemplatePackage.SqlitePackageId, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PacksCanonicalContentWithoutRuntimeDependenciesOrBuildOutput()
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PackagingConstants.TemplatePackage.FoundationPackageId] = "0.18.7",
            [PackagingConstants.TemplatePackage.AppPackageId] = "0.17.11",
            [PackagingConstants.TemplatePackage.SqlitePackageId] = "0.17.23"
        };
        var output = Path.Combine(Path.GetTempPath(), $"koan-template-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        try
        {
            using var prepared = await new TemplatePackageCompiler(RepositoryRoot()).PrepareAsync(
                versions,
                TestContext.Current.CancellationToken);
            await RestoreTemplatePackageAsync();
            await new ProcessRunner().RequireAsync(
                "dotnet",
                [
                    "pack", Path.Combine(RepositoryRoot(), "templates", "Sylin.Koan.Templates.csproj"),
                    "--no-build", "--no-restore", "--nologo", "-p:BuildProjectReferences=false",
                    $"-p:KoanPreparedTemplateRoot={prepared.Root}", "-p:PackageVersion=0.17.999", "-o", output
                ],
                RepositoryRoot(),
                TestContext.Current.CancellationToken);

            var package = Directory.EnumerateFiles(output, "*.nupkg").Single();
            using var archive = ZipFile.OpenRead(package);
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
            Assert.Contains("content/koan-web/.template.config/template.json", entries);
            Assert.Contains("content/koan-console/.template.config/template.json", entries);
            Assert.DoesNotContain(entries, entry => entry.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.Contains("/obj/", StringComparison.OrdinalIgnoreCase));

            var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using (var stream = nuspec.Open())
            {
                var document = XDocument.Load(stream);
                Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "dependency");
            }

            var webProject = archive.GetEntry("content/koan-web/KoanWebApp.csproj")
                ?? throw new InvalidOperationException("Packed web template project is missing.");
            using var reader = new StreamReader(webProject.Open());
            var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.Contains("[0.17.11, 0.18.0)", content, StringComparison.Ordinal);
            Assert.Contains("[0.17.23, 0.18.0)", content, StringComparison.Ordinal);
            Assert.DoesNotContain("__KOAN_", content, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task DirectPackFailsBeforeEmittingAnArtifact()
    {
        var output = Path.Combine(Path.GetTempPath(), $"koan-template-direct-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        try
        {
            await RestoreTemplatePackageAsync();
            var result = await new ProcessRunner().RunAsync(
                "dotnet",
                [
                    "pack", Path.Combine(RepositoryRoot(), "templates", "Sylin.Koan.Templates.csproj"),
                    "--no-build", "--no-restore", "--nologo", "-p:BuildProjectReferences=false", "-o", output
                ],
                RepositoryRoot(),
                TestContext.Current.CancellationToken);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("requires release-compiled package ranges", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(Directory.EnumerateFiles(output), path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DiscoversTheProjectNameChosenByDotnetNew()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-template-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "MyBusinessApp.csproj"), "<Project />");

            Assert.Equal(
                "MyBusinessApp.csproj",
                TemplatePackageProbe.RequireProjectFile(root, PackagingConstants.TemplatePackage.WebShortName));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsAmbiguousGeneratedProjectShapes(int projectCount)
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-template-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            for (var index = 0; index < projectCount; index++)
            {
                File.WriteAllText(Path.Combine(root, $"App{index}.csproj"), "<Project />");
            }

            var error = Assert.Throws<InvalidOperationException>(() =>
                TemplatePackageProbe.RequireProjectFile(root, PackagingConstants.TemplatePackage.WebShortName));

            Assert.Contains($"produced {projectCount}", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private static Task RestoreTemplatePackageAsync() =>
        new ProcessRunner().RequireAsync(
            "dotnet",
            [
                "restore", Path.Combine(RepositoryRoot(), "templates", "Sylin.Koan.Templates.csproj"),
                "--nologo", "-p:BuildProjectReferences=false"
            ],
            RepositoryRoot(),
            TestContext.Current.CancellationToken);
}
