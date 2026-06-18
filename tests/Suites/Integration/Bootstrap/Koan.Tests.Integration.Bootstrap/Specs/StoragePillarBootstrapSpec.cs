using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Storage.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Storage pillar core (per ARCH-0079). Proves <c>IStorageService</c>
/// resolves through real <c>AddKoan()</c> reflective discovery with the Local filesystem
/// connector. See <see cref="DataCorePillarBootstrapSpec"/> for the residual cross-pillar
/// Redis config note (the data connector's eager-connect is a separate concern from ARCH-0080).
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
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Storage:DefaultProfile", "local")
            .WithSetting("Koan:Storage:Profiles:local:Provider", "local")
            .WithSetting("Koan:Storage:Profiles:local:Container", _tempRoot)
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var storage = host.Services.GetRequiredService<IStorageService>();
        storage.Should().NotBeNull();
    }
}
