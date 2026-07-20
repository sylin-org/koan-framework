using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Koan.Packaging.Infrastructure;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class SemanticActivationManifestBuildTests
{
    private const string RootBundlePackageId = "Sylin.Koan.Test.Activation.RootBundle";
    private const string NestedBundlePackageId = "Sylin.Koan.Test.Activation.NestedBundle";
    private const string CapabilityPackageId = "Sylin.Koan.Test.Activation.Capability";
    private const string CompatibilityPackageId = "Sylin.Koan.Test.Activation.Compatibility";
    private const string DownstreamSemanticModuleId = "Sylin.Koan.Test.Downstream";
    private const string S3PackageId = "Sylin.Koan.Storage.Connector.S3";
    private const string ZenGardenContractsPackageId = "Sylin.Koan.ZenGarden.Contracts";
    private const string ZenGardenPackageId = "Sylin.Koan.ZenGarden";
    private const string PackageVersion = "1.0.0";
    private const string NuGetV3Source = "https://api.nuget.org/v3/index.json";

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    [Fact]
    public async Task Source_references_generate_a_versioned_recursive_activation_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-activation-manifest-{Guid.NewGuid():N}");
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);

        try
        {
            var graph = await WriteActivationGraphAsync(Path.Combine(root, "graph"));

            await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"), "System.Console.WriteLine(\"ok\");");
            await File.WriteAllTextAsync(Path.Combine(app, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{graph.RootBundleProject}}" />
                  </ItemGroup>
                  <Import Project="{{CompositionTarget()}}" />
                </Project>
                """);

            var runner = new ProcessRunner();
            await runner.RequireAsync(
                "dotnet",
                ["restore", "Probe.csproj", "--nologo", "--ignore-failed-sources"],
                app,
                TestContext.Current.CancellationToken);
            await runner.RequireAsync(
                "dotnet",
                ["build", "Probe.csproj", "-c", "Release", "--no-restore", "--nologo"],
                app,
                TestContext.Current.CancellationToken);

            var lines = (await File.ReadAllLinesAsync(
                    ReferenceManifestPath(app, "Release"),
                    TestContext.Current.CancellationToken))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            Assert.Equal("schema|1", lines[0]);
            Assert.Equal(
                [$"reference|project|Koan.Activation.RootBundle|{RootBundlePackageId}"],
                lines.Where(line => line.StartsWith("reference|", StringComparison.Ordinal)));
            var dependencyLines = lines
                .Where(line => line.StartsWith("dependency|", StringComparison.Ordinal))
                .ToArray();
            Assert.True(
                ExpectedDependencyLines().SequenceEqual(dependencyLines),
                $"Source dependency graph mismatch.{Environment.NewLine}{string.Join(Environment.NewLine, dependencyLines)}");
            Assert.Equal(5, lines.Length);
            await AssertRequiredManifestMarkerAsync(app);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Staged_package_preserves_the_source_dependency_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-activation-package-manifest-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(app);

        try
        {
            var graph = await WriteActivationGraphAsync(Path.Combine(root, "graph"));
            var config = await WriteLocalFeedConfigAsync(root, feed);
            var runner = new ProcessRunner();

            await runner.RequireAsync(
                "dotnet",
                ["restore", graph.RootBundleProject, "--configfile", config, "--nologo"],
                graph.Root,
                TestContext.Current.CancellationToken);

            foreach (var project in graph.PackOrder)
            {
                await runner.RequireAsync(
                    "dotnet",
                    ["pack", project, "-c", "Release", "--no-restore", "--nologo", "--output", feed],
                    graph.Root,
                    TestContext.Current.CancellationToken);
            }

            var rootPackage = Path.Combine(feed, $"{RootBundlePackageId}.{PackageVersion}.nupkg");
            using (var archive = ZipFile.OpenRead(rootPackage))
            {
                var entry = Assert.Single(
                    archive.Entries,
                    candidate => string.Equals(
                        candidate.FullName,
                        $"buildTransitive/{RootBundlePackageId}.props",
                        StringComparison.Ordinal));
                using var reader = new StreamReader(entry.Open());
                var props = XDocument.Parse(await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
                var dependencies = props
                    .Descendants("KoanActivationEdge")
                    .Select(element => (string?)element.Attribute("Include"))
                    .Where(value => value is not null)
                    .Cast<string>()
                    .ToArray();

                Assert.Equal(ExpectedDependencyEdges(), dependencies);
            }

            await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"), "Console.WriteLine(\"ok\");");
            await File.WriteAllTextAsync(Path.Combine(app, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{{RootBundlePackageId}}" Version="{{PackageVersion}}" />
                  </ItemGroup>
                  <Import Project="{{CompositionTarget()}}" />
                </Project>
                """);

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

            var lines = (await File.ReadAllLinesAsync(
                    ReferenceManifestPath(app, "Release"),
                    TestContext.Current.CancellationToken))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            Assert.Equal("schema|1", lines[0]);
            Assert.Equal(
                [$"reference|package|{RootBundlePackageId}|{RootBundlePackageId}"],
                lines.Where(line => line.StartsWith("reference|", StringComparison.Ordinal)));
            var dependencyLines = lines
                .Where(line => line.StartsWith("dependency|", StringComparison.Ordinal))
                .ToArray();
            Assert.True(
                ExpectedDependencyLines().SequenceEqual(dependencyLines),
                $"Package dependency graph mismatch.{Environment.NewLine}{string.Join(Environment.NewLine, dependencyLines)}");
            Assert.Equal(5, lines.Length);
            await AssertRequiredManifestMarkerAsync(app);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Staged_core_package_executes_its_bundled_registry_generator_downstream()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-core-generator-package-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var consumer = Path.Combine(root, "consumer");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(consumer);

        try
        {
            var runner = new ProcessRunner();
            await runner.RequireAsync(
                "dotnet",
                [
                    "pack",
                    CoreProject(),
                    "-c",
                    BuildConfiguration,
                    "--no-build",
                    "--no-restore",
                    "--nologo",
                    "--output",
                    feed,
                    "-p:IncludeSymbols=false"
                ],
                RepositoryRoot(),
                TestContext.Current.CancellationToken);

            var corePackage = Assert.Single(
                Directory.EnumerateFiles(feed, $"{PackagingConstants.CorePackageId}.*.nupkg"),
                path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase));
            var packageFileName = Path.GetFileName(corePackage);
            var packagePrefix = $"{PackagingConstants.CorePackageId}.";
            Assert.StartsWith(packagePrefix, packageFileName, StringComparison.Ordinal);
            var coreVersion = packageFileName[packagePrefix.Length..^".nupkg".Length];
            var config = await WritePackageConsumerConfigAsync(root, feed);

            await File.WriteAllTextAsync(Path.Combine(consumer, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <PackageId>{{DownstreamSemanticModuleId}}</PackageId>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
                    <KoanComposition>false</KoanComposition>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{{PackagingConstants.CorePackageId}}" Version="{{coreVersion}}" />
                    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.8" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(consumer, "DownstreamModule.cs"), $$"""
                using System;
                using Koan.Core;
                using Koan.Core.Composition;
                using Koan.Core.Semantics;
                using Koan.Core.Semantics.Contributions;

                namespace Downstream;

                public sealed class DownstreamModule : KoanModule, IContributeTo<DownstreamTarget>
                {
                    public void Contribute(DownstreamTarget target) => target.Applied = true;

                    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
                        => composition.AddObservation(
                            "koan.test.downstream.evidence",
                            "downstream:evidence",
                            "The retained downstream module reported evidence.",
                            "retained-module",
                            Id);
                }

                public sealed class DownstreamTarget
                {
                    public bool Applied { get; set; }
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"), """
                using Koan.Core;
                using Koan.Core.Diagnostics;
                using Koan.Core.Hosting.Runtime;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;

                var builder = Host.CreateApplicationBuilder(args);
                builder.Services.AddKoan();
                using var host = builder.Build();
                await host.StartAsync();
                host.Services.GetRequiredService<IAppRuntime>().Discover();
                var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;
                if (!facts.Any(fact =>
                        fact.Code == "koan.test.downstream.evidence"
                        && fact.Subject == "downstream:evidence"))
                {
                    return 7;
                }

                await host.StopAsync();
                Console.WriteLine("TRIMMED-MODULE-EVIDENCE-OK");
                return 0;
                """);

            var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;

            await runner.RequireAsync(
                "dotnet",
                ["restore", "Probe.csproj", "--runtime", runtimeIdentifier, "--configfile", config, "--no-cache", "--nologo"],
                consumer,
                TestContext.Current.CancellationToken);
            await runner.RequireAsync(
                "dotnet",
                ["build", "Probe.csproj", "-c", "Release", "--no-restore", "--nologo"],
                consumer,
                TestContext.Current.CancellationToken);

            var generatedFile = Assert.Single(
                Directory.EnumerateFiles(
                    Path.Combine(consumer, "obj", "generated"),
                    "KoanRegistry_*.g.cs",
                    SearchOption.AllDirectories));
            var generated = await File.ReadAllTextAsync(
                generatedFile,
                TestContext.Current.CancellationToken);

            Assert.Contains("RegisterSemanticModule(", generated, StringComparison.Ordinal);
            Assert.Contains($"\"{DownstreamSemanticModuleId}\"", generated, StringComparison.Ordinal);
            Assert.Contains("typeof(global::Downstream.DownstreamModule)", generated, StringComparison.Ordinal);
            Assert.Contains("SemanticContributionBinding", generated, StringComparison.Ordinal);
            Assert.Contains("IContributeTo<global::Downstream.DownstreamTarget>", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("IKoanCompositionContributor", generated, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(generated, "RegisterSemanticModule("));

            var publish = Path.Combine(consumer, "publish");
            await runner.RequireAsync(
                "dotnet",
                [
                    "publish",
                    "Probe.csproj",
                    "-c", "Release",
                    "--runtime", runtimeIdentifier,
                    "--self-contained", "true",
                    "--no-restore",
                    "--nologo",
                    "--output", publish,
                    "-p:PublishTrimmed=true"
                ],
                consumer,
                TestContext.Current.CancellationToken);
            var trimmedRun = await runner.RequireAsync(
                "dotnet",
                [Path.Combine(publish, "Probe.dll")],
                consumer,
                TestContext.Current.CancellationToken);
            Assert.Contains("TRIMMED-MODULE-EVIDENCE-OK", trimmedRun, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Advertised_communication_dependents_follow_ordinary_reference_paths_without_activation_metadata()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "src");
        var projects = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var projectSet = projects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordinaryEdges = projects.ToDictionary(
            static project => project,
            project => ProjectReferences(project, projectSet),
            StringComparer.OrdinalIgnoreCase);
        var communication = Path.GetFullPath(Path.Combine(
            sourceRoot,
            "Koan.Communication",
            "Koan.Communication.csproj"));
        var consumers = ExpectedCommunicationDependents()
            .Select(path => Path.GetFullPath(Path.Combine(RepositoryRoot(), path)))
            .ToArray();
        var missing = consumers
            .Where(project => !projectSet.Contains(project) || !Reaches(project, communication, ordinaryEdges))
            .Select(project => Path.GetRelativePath(RepositoryRoot(), project).Replace('\\', '/'))
            .ToArray();
        var authoredActivationMetadata = projects
            .SelectMany(project => XDocument.Load(project).Descendants("ProjectReference"))
            .SelectMany(reference => reference.Attributes())
            .Where(attribute => attribute.Name.LocalName.StartsWith("KoanActivation", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(consumers);
        Assert.True(
            missing.Length == 0,
            "The Communication dependent conformance table contains a package that no longer carries " +
            $"Communication through ordinary references. Reclassify it deliberately: {string.Join(", ", missing)}");
        Assert.Empty(authoredActivationMetadata);
    }

    [Fact]
    public async Task Conditional_reference_manifests_are_isolated_by_configuration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-activation-config-{Guid.NewGuid():N}");
        var app = Path.Combine(root, "app");
        var debugCapability = Path.Combine(root, "debug-capability");
        var releaseCapability = Path.Combine(root, "release-capability");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(debugCapability);
        Directory.CreateDirectory(releaseCapability);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.targets"), $$"""
                <Project>
                  <Import Project="{{ActivationTarget()}}"
                          Condition="'$(KoanSemanticActivationTargetsImported)' != 'true'" />
                </Project>
                """);
            await WriteLibraryAsync(
                debugCapability,
                "DebugCapability.csproj",
                "Sylin.Koan.Test.Configuration.Debug");
            await WriteLibraryAsync(
                releaseCapability,
                "ReleaseCapability.csproj",
                "Sylin.Koan.Test.Configuration.Release");
            await File.WriteAllTextAsync(
                Path.Combine(app, "Program.cs"),
                "System.Console.WriteLine(\"ok\");");
            await File.WriteAllTextAsync(Path.Combine(app, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../debug-capability/DebugCapability.csproj"
                                      Condition="'$(Configuration)' == 'Debug'" />
                    <ProjectReference Include="../release-capability/ReleaseCapability.csproj"
                                      Condition="'$(Configuration)' == 'Release'" />
                  </ItemGroup>
                  <Import Project="{{CompositionTarget()}}" />
                </Project>
                """);

            var runner = new ProcessRunner();
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                await runner.RequireAsync(
                    "dotnet",
                    ["build", "Probe.csproj", "-c", configuration, "--nologo"],
                    app,
                    TestContext.Current.CancellationToken);
            }

            var debugManifest = await File.ReadAllTextAsync(
                ReferenceManifestPath(app, "Debug"),
                TestContext.Current.CancellationToken);
            var releaseManifest = await File.ReadAllTextAsync(
                ReferenceManifestPath(app, "Release"),
                TestContext.Current.CancellationToken);

            Assert.Contains("Sylin.Koan.Test.Configuration.Debug", debugManifest, StringComparison.Ordinal);
            Assert.DoesNotContain("Sylin.Koan.Test.Configuration.Release", debugManifest, StringComparison.Ordinal);
            Assert.Contains("Sylin.Koan.Test.Configuration.Release", releaseManifest, StringComparison.Ordinal);
            Assert.DoesNotContain("Sylin.Koan.Test.Configuration.Debug", releaseManifest, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task S3_contract_dependency_does_not_activate_the_ZenGarden_implementation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-zengarden-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), "System.Console.WriteLine(\"ok\");");
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{Path.Combine(RepositoryRoot(), "src", "Connectors", "Storage", "S3", "Koan.Storage.Connector.S3.csproj")}}" />
                  </ItemGroup>
                  <Import Project="{{CompositionTarget()}}" />
                </Project>
                """);

            var runner = new ProcessRunner();
            await runner.RequireAsync(
                "dotnet",
                ["restore", "Probe.csproj", "--nologo", "--ignore-failed-sources"],
                root,
                TestContext.Current.CancellationToken);
            await runner.RequireAsync(
                "dotnet",
                ["build", "Probe.csproj", "-c", "Release", "--no-restore", "--nologo"],
                root,
                TestContext.Current.CancellationToken);

            var manifest = await File.ReadAllLinesAsync(
                ReferenceManifestPath(root, "Release"),
                TestContext.Current.CancellationToken);
            Assert.Contains($"reference|project|Koan.Storage.Connector.S3|{S3PackageId}", manifest);
            Assert.Contains(manifest, line => line.Split('|').Contains(ZenGardenContractsPackageId, StringComparer.Ordinal));
            Assert.DoesNotContain(manifest, line => line.Split('|').Contains(ZenGardenPackageId, StringComparer.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Weaviate_explicit_ZenGarden_intent_fails_without_autonomous_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-weaviate-explicit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{WeaviateProject()}}" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), """
                using Koan.Core.Adapters;
                using Koan.Core.Orchestration;
                using Koan.Core.Orchestration.Abstractions;
                using Koan.Data.Vector.Connector.Weaviate;
                using Microsoft.Extensions.Configuration;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Options;

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Koan:Data:Weaviate:ConnectionString"] = "zen-garden://weaviate"
                    })
                    .Build();
                var discovery = new ProbeDiscoveryCoordinator();
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddSingleton<IOptions<AdaptersReadinessOptions>>(
                    Options.Create(new AdaptersReadinessOptions()));
                services.AddSingleton<IServiceDiscoveryCoordinator>(discovery);
                new Koan.Data.Vector.Connector.Weaviate.Initialization.WeaviateVectorModule().Register(services);

                using var provider = services.BuildServiceProvider();
                try
                {
                    _ = provider.GetRequiredService<IOptions<WeaviateOptions>>().Value;
                    return 2;
                }
                catch (InvalidOperationException exception)
                {
                    if (!exception.Message.Contains("Weaviate explicit Zen Garden intent", StringComparison.Ordinal)
                        || discovery.AutomaticCalls != 0
                        || discovery.RequiredCalls != 1)
                    {
                        return 3;
                    }

                    Console.WriteLine("WEAVIATE-EXPLICIT-REJECTED");
                    return 0;
                }

                sealed class ProbeDiscoveryCoordinator : IServiceDiscoveryCoordinator
                {
                    public int AutomaticCalls { get; private set; }
                    public int RequiredCalls { get; private set; }

                    public Task<AdapterDiscoveryResult> DiscoverService(
                        string serviceName,
                        DiscoveryContext? context = null,
                        CancellationToken cancellationToken = default)
                    {
                        AutomaticCalls++;
                        return Task.FromResult(AdapterDiscoveryResult.Failed(serviceName, "unexpected automatic discovery"));
                    }

                    public Task<AdapterDiscoveryResult> ResolveServiceIntent(
                        string serviceName,
                        string intent,
                        DiscoveryContext? context = null,
                        CancellationToken cancellationToken = default)
                    {
                        RequiredCalls++;
                        return Task.FromResult(AdapterDiscoveryResult.Failed(
                            serviceName,
                            "No ready Weaviate offering matched the explicit intent."));
                    }

                    public IServiceDiscoveryAdapter[] GetRegisteredAdapters() => [];
                }
                """);

            var runner = new ProcessRunner();
            await runner.RequireAsync(
                "dotnet",
                ["restore", "Probe.csproj", "--nologo", "--ignore-failed-sources"],
                root,
                TestContext.Current.CancellationToken);
            var result = await runner.RequireAsync(
                "dotnet",
                ["run", "--project", "Probe.csproj", "-c", "Release", "--no-restore", "--nologo"],
                root,
                TestContext.Current.CancellationToken);
            Assert.Contains("WEAVIATE-EXPLICIT-REJECTED", result, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<ActivationGraph> WriteActivationGraphAsync(string root)
    {
        var rootBundle = Path.Combine(root, "root-bundle");
        var nestedBundle = Path.Combine(root, "nested-bundle");
        var capability = Path.Combine(root, "capability");
        var compatibility = Path.Combine(root, "compatibility");

        Directory.CreateDirectory(rootBundle);
        Directory.CreateDirectory(nestedBundle);
        Directory.CreateDirectory(capability);
        Directory.CreateDirectory(compatibility);

        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.targets"), $$"""
            <Project>
              <Import Project="{{ActivationTarget()}}"
                      Condition="'$(KoanSemanticActivationTargetsImported)' != 'true'" />
            </Project>
            """);

        var capabilityProject = await WriteLibraryAsync(
            capability,
            "Koan.Activation.Capability.csproj",
            CapabilityPackageId);
        var compatibilityProject = await WriteLibraryAsync(
            compatibility,
            "Koan.Activation.Compatibility.csproj",
            CompatibilityPackageId);
        var nestedBundleProject = await WriteLibraryAsync(
            nestedBundle,
            "Koan.Activation.NestedBundle.csproj",
            NestedBundlePackageId,
            """
            <ItemGroup>
              <ProjectReference Include="../capability/Koan.Activation.Capability.csproj" />
            </ItemGroup>
            """);
        var rootBundleProject = await WriteLibraryAsync(
            rootBundle,
            "Koan.Activation.RootBundle.csproj",
            RootBundlePackageId,
            """
            <ItemGroup>
              <ProjectReference Include="../nested-bundle/Koan.Activation.NestedBundle.csproj" />
              <ProjectReference Include="../compatibility/Koan.Activation.Compatibility.csproj" />
            </ItemGroup>
            """);

        return new ActivationGraph(
            root,
            rootBundleProject,
            [capabilityProject, compatibilityProject, nestedBundleProject, rootBundleProject]);
    }

    private static async Task AssertRequiredManifestMarkerAsync(string app)
    {
        var assemblyInfo = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(app, "obj", "Release"),
            "*.AssemblyInfo.cs",
            SearchOption.AllDirectories));
        var generated = await File.ReadAllTextAsync(
            assemblyInfo,
            TestContext.Current.CancellationToken);
        Assert.Contains(
            "AssemblyMetadata(\"KoanSemanticActivationManifest\", \"1\")",
            generated,
            StringComparison.Ordinal);
    }

    private static async Task<string> WriteLocalFeedConfigAsync(string root, string feed)
    {
        var path = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(path, $$"""
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
        return path;
    }

    private static async Task<string> WritePackageConsumerConfigAsync(string root, string feed)
    {
        var path = Path.Combine(root, "NuGet.Consumer.Config");
        var document = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "config",
                    new XElement(
                        "add",
                        new XAttribute("key", "globalPackagesFolder"),
                        new XAttribute("value", Path.Combine(root, "consumer-packages")))),
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement(
                        "add",
                        new XAttribute("key", "staged"),
                        new XAttribute("value", feed)),
                    new XElement(
                        "add",
                        new XAttribute("key", "nuget.org"),
                        new XAttribute("value", NuGetV3Source),
                        new XAttribute("protocolVersion", "3")))));
        await File.WriteAllTextAsync(
            path,
            document.ToString(),
            TestContext.Current.CancellationToken);
        return path;
    }

    private static string[] ExpectedDependencyEdges() =>
    [
        $"{NestedBundlePackageId}|{CapabilityPackageId}",
        $"{RootBundlePackageId}|{CompatibilityPackageId}",
        $"{RootBundlePackageId}|{NestedBundlePackageId}"
    ];

    private static int CountOccurrences(string value, string pattern)
        => value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static string[] ExpectedDependencyLines() =>
        ExpectedDependencyEdges().Select(edge => $"dependency|{edge}").ToArray();

    private static async Task<string> WriteLibraryAsync(
        string directory,
        string projectName,
        string packageId,
        string projectItems = "")
    {
        var project = Path.Combine(directory, projectName);
        await File.WriteAllTextAsync(
            project,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>{{packageId}}</PackageId>
                <PackageVersion>{{PackageVersion}}</PackageVersion>
              </PropertyGroup>
              {{projectItems}}
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Marker.cs"),
            $"namespace {Path.GetFileNameWithoutExtension(projectName).Replace('.', '_')}; public sealed class Marker {{ }}");
        return project;
    }

    private static string CompositionTarget() =>
        Path.Combine(RepositoryRoot(), "src", "Koan.Core", "build", "Sylin.Koan.Core.targets");

    private static string ReferenceManifestPath(string projectDirectory, string configuration) =>
        Path.Combine(projectDirectory, "obj", configuration, "net10.0", "koan.references.manifest");

    private static string ActivationTarget() =>
        Path.Combine(
            RepositoryRoot(),
            "src",
            "Koan.Core",
            "build",
            "tools",
            "Sylin.Koan.SemanticActivation.targets");

    private static string CoreProject() =>
        Path.Combine(RepositoryRoot(), "src", "Koan.Core", "Koan.Core.csproj");

    private static string WeaviateProject() =>
        Path.Combine(
            RepositoryRoot(),
            "src",
            "Connectors",
            "Data",
            "Vector",
            "Weaviate",
            "Koan.Data.Vector.Connector.Weaviate.csproj");

    private static string[] ProjectReferences(
        string project,
        IReadOnlySet<string> knownProjects)
    {
        return XDocument.Load(project)
            .Descendants("ProjectReference")
            .Where(reference => !string.Equals(
                (string?)reference.Attribute("ReferenceOutputAssembly"),
                "false",
                StringComparison.OrdinalIgnoreCase))
            .Where(reference => !string.Equals(
                (string?)reference.Attribute("OutputItemType"),
                "Analyzer",
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(project)!,
                NormalizeProjectReferenceInclude((string)reference.Attribute("Include")!))))
            .Where(knownProjects.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeProjectReferenceInclude(string include) =>
        include
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static string[] ExpectedCommunicationDependents() =>
    [
        "src/Koan.Cache/Koan.Cache.csproj",
        "src/Koan.Jobs/Koan.Jobs.csproj",
        "src/Connectors/Communication/RabbitMq/Koan.Communication.Connector.RabbitMq.csproj",
        "src/Koan.Cache.Adapter.Redis/Koan.Cache.Adapter.Redis.csproj",
        "src/Koan.Cache.Adapter.Sqlite/Koan.Cache.Adapter.Sqlite.csproj",
        "src/Koan.Mcp.Operations/Koan.Mcp.Operations.csproj",
    ];

    private static bool Reaches(
        string source,
        string target,
        IReadOnlyDictionary<string, string[]> edges)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(source);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            if (current.Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
            if (!edges.TryGetValue(current, out var children)) continue;
            foreach (var child in children) pending.Push(child);
        }

        return false;
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed record ActivationGraph(
        string Root,
        string RootBundleProject,
        IReadOnlyList<string> PackOrder);
}
