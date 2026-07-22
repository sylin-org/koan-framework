using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class Wave0PackageConsumerTests
{
    private static readonly string[] Owners =
    [
        "Sylin.Koan.Testing.Hosting",
        "Sylin.Koan.Testing",
        "Sylin.Koan.Testing.Containers",
        "Sylin.Koan.Cache.Adapter.Sqlite",
        "Sylin.Koan.Data.SoftDelete",
        "Sylin.Koan.Web.Admin",
        "Sylin.Koan.Web.Auth.Connector.Test"
    ];

    [Fact]
    public async Task Candidate_packages_restore_compile_and_run_outside_the_repository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-wave0-consumer-{Guid.CreateVersion7():N}");
        var feed = Path.Combine(root, "feed");
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(app);

        try
        {
            var repository = RepositoryRoot();
            var runner = new ProcessRunner();
            var packages = await PromotionClosureAsync(
                repository,
                runner,
                TestContext.Current.CancellationToken);
            foreach (var package in packages)
            {
                await runner.RequireAsync(
                    "dotnet",
                    ["pack", package.ProjectPath, "-c", "Release", "--no-restore", "-p:PublicRelease=true", "-o", feed, "--nologo"],
                    repository,
                    TestContext.Current.CancellationToken);
            }

            var versions = Owners.ToDictionary(
                packageId => packageId,
                packageId => ReadPackedVersion(feed, packageId),
                StringComparer.OrdinalIgnoreCase);
            var references = new StringBuilder();
            foreach (var packageId in Owners)
            {
                references.AppendLine(
                    $"    <PackageReference Include=\"{packageId}\" Version=\"{versions[packageId]}\" />");
            }

            await File.WriteAllTextAsync(Path.Combine(app, "Wave0Consumer.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                {{references.ToString().TrimEnd()}}
                  </ItemGroup>
                </Project>
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"), """
                using Koan.Cache.Adapter.Sqlite.Options;
                using Koan.Core;
                using Koan.Data.Core.Model;
                using Koan.Data.SoftDelete;
                using Koan.Testing;
                using Koan.Testing.Containers;
                using Koan.Testing.Integration;
                using Koan.Web.Admin.Options;
                using Koan.Web.Auth.Connector.Test.Options;

                await using (var host = await KoanIntegrationHost.Configure()
                    .ConfigureServices(services => services.AddKoan())
                    .StartAsync())
                {
                    _ = host.Services;
                }

                await using (var fixture = new InMemoryFixture())
                {
                    await fixture.InitializeAsync();
                    if (!fixture.IsAvailable) throw new InvalidOperationException(fixture.Reason);
                }

                _ = typeof(EntityConformanceSpecs<ConsumerEntity>);
                _ = new SqliteCacheOptions();
                _ = new KoanAdminOptions();
                _ = new TestProviderOptions();
                Console.WriteLine("WAVE0|PACKAGE-CONSUMER|PASS");

                [SoftDelete]
                sealed class ConsumerEntity : Entity<ConsumerEntity>;
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(app, "NuGet.config"), $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="wave0" value="{{SecurityElement.Escape(feed)}}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """, TestContext.Current.CancellationToken);

            await runner.RequireAsync(
                "dotnet",
                ["restore", "Wave0Consumer.csproj", "--configfile", "NuGet.config", "--packages", Path.Combine(root, "packages"), "--nologo"],
                app,
                TestContext.Current.CancellationToken);
            await runner.RequireAsync(
                "dotnet",
                ["build", "Wave0Consumer.csproj", "-c", "Release", "--no-restore", "--nologo"],
                app,
                TestContext.Current.CancellationToken);
            var output = await runner.RequireAsync(
                "dotnet",
                ["run", "--project", "Wave0Consumer.csproj", "-c", "Release", "--no-build"],
                app,
                TestContext.Current.CancellationToken);

            Assert.Contains("WAVE0|PACKAGE-CONSUMER|PASS", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadPackedVersion(string feed, string packageId)
    {
        var packages = Directory.EnumerateFiles(feed, $"{packageId}.*.nupkg")
            .Where(path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Where(path => char.IsAsciiDigit(Path.GetFileName(path)[packageId.Length + 1]))
            .ToArray();
        var package = Assert.Single(packages);
        var file = Path.GetFileName(package);
        return file[(packageId.Length + 1)..^".nupkg".Length];
    }

    private static async Task<IReadOnlyList<PackageProject>> PromotionClosureAsync(
        string repository,
        ProcessRunner runner,
        CancellationToken cancellationToken)
    {
        var packages = await new RepositoryInspector(repository, runner)
            .DiscoverPackagesAsync(cancellationToken);
        var byId = packages.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        var byProject = packages.ToDictionary(
            package => Path.GetFullPath(Path.Combine(repository, package.ProjectPath)),
            StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, PackageProject>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<PackageProject>();

        foreach (var packageId in Owners)
        {
            Assert.True(byId.TryGetValue(packageId, out var package),
                $"Package owner '{packageId}' must be discoverable.");
            pending.Enqueue(package!);
        }

        while (pending.TryDequeue(out var package))
        {
            if (!selected.TryAdd(package.PackageId, package)) continue;

            foreach (var reference in package.ProjectReferences)
            {
                if (byProject.TryGetValue(Path.GetFullPath(reference), out var dependency))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        return selected.Values
            .OrderBy(package => package.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
