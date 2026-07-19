using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class DirectReferenceManifestBuildTests
{
    [Fact]
    public async Task Package_and_project_references_remain_direct_in_a_fully_isolated_restore_graph()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-reference-manifest-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var app = Path.Combine(root, "app");
        var missingApp = Path.Combine(root, "missing-app");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(missingApp);

        try
        {
            WritePackage(feed);
            var repository = RepositoryRoot();
            var activationTarget = Path.Combine(
                repository,
                "src",
                "Koan.Core",
                "build",
                "tools",
                "Sylin.Koan.SemanticActivation.targets");
            var coreProject = await WriteProjectAsync(
                Path.Combine(root, "projects", "core"),
                activationTarget);
            var compositionTarget = Path.Combine(
                repository,
                "src",
                "Koan.Core",
                "build",
                "Sylin.Koan.Core.targets");
            await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"), "Console.WriteLine(\"ok\");");
            await File.WriteAllTextAsync(Path.Combine(app, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{coreProject}}" />
                    <PackageReference Include="Sylin.Koan.Communication" Version="1.0.0" />
                  </ItemGroup>
                  <Import Project="{{compositionTarget}}" />
                </Project>
                """);
            AssertProjectGraphIsContained(root, Path.Combine(app, "Probe.csproj"));

            var config = Path.Combine(root, "NuGet.Config");
            await File.WriteAllTextAsync(config, $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{{Path.Combine(root, "packages")}}" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="local" value="{{feed}}" />
                  </packageSources>
                </configuration>
                """);

            var runner = new ProcessRunner();
            await runner.RequireAsync(
                "dotnet",
                ["restore", "Probe.csproj", "--configfile", config, "--nologo"],
                app,
                TestContext.Current.CancellationToken);
            await runner.RequireAsync(
                "dotnet",
                ["build", "Probe.csproj", "-c", "Release", "--no-restore", "--nologo"],
                app,
                TestContext.Current.CancellationToken);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(app, "koan.lock.json")));
            var rootElement = document.RootElement;
            Assert.Equal(2, rootElement.GetProperty("schema").GetInt32());
            var direct = rootElement.GetProperty("directReferences")
                .EnumerateArray()
                .Select(reference => $"{reference.GetProperty("kind").GetString()}|{reference.GetProperty("id").GetString()}")
                .ToArray();
            Assert.Equal(
                ["package|Sylin.Koan.Communication", "project|Koan.Core"],
                direct);

            await File.WriteAllTextAsync(Path.Combine(missingApp, "Probe.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Sylin.Koan.DeliberatelyMissing" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """);
            AssertProjectGraphIsContained(root, Path.Combine(missingApp, "Probe.csproj"));

            var missing = await runner.RunAsync(
                "dotnet",
                ["restore", "Probe.csproj", "--configfile", config, "--no-cache", "--nologo"],
                missingApp,
                TestContext.Current.CancellationToken);

            Assert.NotEqual(0, missing.ExitCode);
            Assert.Contains("Sylin.Koan.DeliberatelyMissing", missing.StandardError + missing.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<string> WriteProjectAsync(string directory, string activationTarget)
    {
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Koan.Core.csproj");
        await File.WriteAllTextAsync(project, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>Koan.Core</AssemblyName>
                <PackageId>Sylin.Koan.Core</PackageId>
              </PropertyGroup>
              <Import Project="{{activationTarget}}" />
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Marker.cs"),
            "namespace Koan.Core; public sealed class DirectReferenceFixtureMarker { }");
        return project;
    }

    private static void AssertProjectGraphIsContained(string root, string entryProject)
    {
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(Path.GetFullPath(entryProject));

        while (pending.TryPop(out var project))
        {
            Assert.StartsWith(rootPath, project, StringComparison.OrdinalIgnoreCase);
            if (!visited.Add(project)) continue;

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = Assert.IsType<XAttribute>(reference.Attribute("Include"));
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Value)));
            }
        }
    }

    private static void WritePackage(string feed)
    {
        var path = Path.Combine(feed, "Sylin.Koan.Communication.1.0.0.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("Sylin.Koan.Communication.nuspec");
        using (var writer = new StreamWriter(entry.Open()))
        {
            writer.Write("""
                <?xml version="1.0"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Sylin.Koan.Communication</id>
                    <version>1.0.0</version>
                    <authors>Koan</authors>
                    <description>Direct-reference manifest build fixture.</description>
                  </metadata>
                </package>
                """);
        }

        var activation = archive.CreateEntry("buildTransitive/Sylin.Koan.Communication.props");
        using var activationWriter = new StreamWriter(activation.Open());
        activationWriter.Write("""
            <Project>
              <ItemGroup>
                <KoanActivationNode Include="Sylin.Koan.Communication" />
              </ItemGroup>
            </Project>
            """);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
