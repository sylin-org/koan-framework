using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using Koan.Packaging.Infrastructure;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class Wave0PackageConsumerTests
{
    private static readonly (string PackageId, string ProjectPath)[] Owners =
    [
        ("Sylin.Koan.Testing.Hosting", "src/Koan.Testing.Hosting/Koan.Testing.Hosting.csproj"),
        ("Sylin.Koan.Testing", "src/Koan.Testing/Koan.Testing.csproj"),
        ("Sylin.Koan.Testing.Containers", "src/Koan.Testing.Containers/Koan.Testing.Containers.csproj"),
        ("Sylin.Koan.Cache.Adapter.Sqlite", "src/Koan.Cache.Adapter.Sqlite/Koan.Cache.Adapter.Sqlite.csproj"),
        ("Sylin.Koan.Data.SoftDelete", "src/Koan.Data.SoftDelete/Koan.Data.SoftDelete.csproj"),
        ("Sylin.Koan.Web.Admin", "src/Koan.Web.Admin/Koan.Web.Admin.csproj"),
        ("Sylin.Koan.Web.Auth.Connector.Test", "src/Connectors/Web/Auth/Test/Koan.Web.Auth.Connector.Test.csproj")
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
            foreach (var owner in Owners)
            {
                await runner.RequireAsync(
                    "dotnet",
                    ["pack", owner.ProjectPath, "-c", "Release", "--no-restore", "-p:PublicRelease=true", "-o", feed, "--nologo"],
                    repository,
                    TestContext.Current.CancellationToken);
            }

            var versions = Owners.ToDictionary(
                owner => owner.PackageId,
                owner => ReadPackedVersion(feed, owner.PackageId),
                StringComparer.OrdinalIgnoreCase);
            var references = new StringBuilder();
            foreach (var owner in Owners)
            {
                references.AppendLine(
                    $"    <PackageReference Include=\"{owner.PackageId}\" Version=\"{versions[owner.PackageId]}\" />");
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

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
