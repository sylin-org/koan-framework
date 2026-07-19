using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class DirectReferenceManifestBuildTests
{
    [Fact]
    public async Task Package_and_project_references_remain_direct_while_the_resolved_graph_stays_separate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-reference-manifest-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(app);

        try
        {
            WritePackage(feed);
            var repository = RepositoryRoot();
            var coreProject = Path.Combine(repository, "src", "Koan.Core", "Koan.Core.csproj");
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
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
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
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
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
