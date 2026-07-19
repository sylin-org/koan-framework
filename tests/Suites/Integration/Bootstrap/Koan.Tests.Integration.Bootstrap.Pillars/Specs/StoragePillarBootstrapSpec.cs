using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Storage.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Boot-smoke for the Storage pillar core (per ARCH-0079). Proves <c>IStorageService</c>
/// resolves through real <c>AddKoan()</c> reflective discovery with the Local filesystem
/// connector.
/// </summary>
public sealed class StoragePillarBootstrapSpec : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempRoot;

    public StoragePillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), $"koan-storage-bootsmoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task AddKoan_resolves_IStorageService_through_real_bootstrap()
    {
        await using var host = await PillarHost.Configure()
            .WithSetting("Koan:Storage:DefaultProfile", "local")
            .WithSetting("Koan:Storage:Profiles:local:Provider", "local")
            .WithSetting("Koan:Storage:Profiles:local:Container", "files")
            .WithSetting("Koan:Storage:Providers:Local:BasePath", _tempRoot)
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var storage = host.Services.GetRequiredService<IStorageService>();
        storage.Should().NotBeNull();

        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;
        facts.Should().ContainSingle(fact =>
            fact.Code == "koan.storage.profile.selected"
            && fact.Subject == "storage:profile:local"
            && fact.Summary.Contains("local", StringComparison.OrdinalIgnoreCase));
        facts.Should().ContainSingle(fact =>
            fact.Code == "koan.storage.default.resolved"
            && fact.Subject == "storage:default-profile");
    }
}
