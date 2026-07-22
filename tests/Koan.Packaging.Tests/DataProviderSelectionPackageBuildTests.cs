using System.IO.Compression;
using System.Runtime.CompilerServices;
using Koan.Packaging.Infrastructure;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class DataProviderSelectionPackageBuildTests
{
    [Theory]
    [InlineData(false, "floor|built-in-floor|False")]
    [InlineData(true, "direct|direct-reference-intent|True")]
    public async Task AddKoan_honors_direct_package_intent_but_not_transitive_provider_presence(
        bool includeDirect,
        string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-data-selection-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(app);

        try
        {
            WritePackage(feed, "Sylin.Koan.Data.Connector.Direct");
            WritePackage(feed, "Sylin.Koan.Data.Connector.Transitive");
            WritePackage(
                feed,
                "Sylin.Koan.Bundle.Probe",
                dependency: "Sylin.Koan.Data.Connector.Transitive");

            var repository = RepositoryRoot();
            var core = Path.Combine(repository, "src", "Koan.Core", "Koan.Core.csproj");
            var dataAbstractions = Path.Combine(repository, "src", "Koan.Data.Abstractions", "Koan.Data.Abstractions.csproj");
            var dataCore = Path.Combine(repository, "src", "Koan.Data.Core", "Koan.Data.Core.csproj");
            var compositionTarget = Path.Combine(repository, "src", "Koan.Core", "build", "Sylin.Koan.Core.targets");

            await File.WriteAllTextAsync(Path.Combine(app, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{core}}" />
                    <ProjectReference Include="{{dataAbstractions}}" />
                    <ProjectReference Include="{{dataCore}}" />
                    <PackageReference Include="Sylin.Koan.Bundle.Probe" Version="1.0.0" />
                    <PackageReference Include="Sylin.Koan.Data.Connector.Direct" Version="1.0.0"
                                      Condition="'$(IncludeDirect)' == 'true'" />
                  </ItemGroup>
                  <Import Project="{{compositionTarget}}" />
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"), """
                using Koan.Core;
                using Koan.Data.Abstractions;
                using Koan.Data.Abstractions.Naming;
                using Koan.Data.Core.Routing;
                using Microsoft.Extensions.Configuration;
                using Microsoft.Extensions.DependencyInjection;

                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddSingleton<IDataAdapterFactory, FloorFactory>();
                services.AddSingleton<IDataAdapterFactory, TransitiveFactory>();
                services.AddSingleton<IDataAdapterFactory, DirectFactory>();
                services.AddKoan();
                using var provider = services.BuildServiceProvider();
                var selected = provider.GetRequiredService<DataDefaultProviderPlan>();
                Console.WriteLine($"RESULT|{selected.ProviderId}|{selected.Receipt.Reason}|{selected.Receipt.DirectIntent}");

                abstract class ProbeFactory(string provider, bool floor, string reference) : IDataAdapterFactory
                {
                    public string Provider { get; } = provider;
                    public bool IsAutomaticFloor { get; } = floor;
                    public IReadOnlyCollection<string> ReferenceIdentities { get; } = [reference];
                    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = "Default")
                        where TEntity : class, IEntity<TKey> where TKey : notnull => throw new NotSupportedException();
                    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
                }

                sealed class FloorFactory() : ProbeFactory("floor", true, "Sylin.Koan.Foundation.Floor");
                [ProviderPriority(1000)]
                sealed class TransitiveFactory() : ProbeFactory("transitive", false, "Sylin.Koan.Data.Connector.Transitive");
                [ProviderPriority(-1000)]
                sealed class DirectFactory() : ProbeFactory("direct", false, "Sylin.Koan.Data.Connector.Direct");
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

            var property = $"-p:IncludeDirect={includeDirect.ToString().ToLowerInvariant()}";
            var runner = new ProcessRunner();
            await runner.RequireAsync(
                "dotnet",
                ["restore", "Probe.csproj", "--configfile", config, "--no-dependencies", "--nologo", property],
                app,
                TestContext.Current.CancellationToken);
            await runner.RequireAsync(
                "dotnet",
                ["build", "Probe.csproj", "-c", "Release", "--no-restore", "--nologo", property],
                app,
                TestContext.Current.CancellationToken);
            var output = await runner.RequireAsync(
                "dotnet",
                ["run", "--project", "Probe.csproj", "-c", "Release", "--no-build", property],
                app,
                TestContext.Current.CancellationToken);

            Assert.Contains($"RESULT|{expected}", output, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void WritePackage(string feed, string id, string? dependency = null)
    {
        var path = Path.Combine(feed, $"{id}.1.0.0.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{id}.nuspec");
        var dependencies = dependency is null
            ? ""
            : $"<dependencies><group targetFramework=\"net10.0\"><dependency id=\"{dependency}\" version=\"1.0.0\" /></group></dependencies>";
        using (var writer = new StreamWriter(entry.Open()))
        {
            writer.Write($$"""
                <?xml version="1.0"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{{id}}</id>
                    <version>1.0.0</version>
                    <authors>Koan</authors>
                    <description>Provider-selection manifest fixture.</description>
                    {{dependencies}}
                  </metadata>
                </package>
                """);
        }

        var activation = archive.CreateEntry($"buildTransitive/{id}.props");
        using var activationWriter = new StreamWriter(activation.Open());
        activationWriter.Write($$"""
            <Project>
              <ItemGroup>
                <KoanActivationNode Include="{{id}}" />
              </ItemGroup>
            </Project>
            """);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
