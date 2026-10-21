using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// NK-059: an application-supplied pillar service must not silently remove that pillar's hard-segmentation
/// realization. A supplied <see cref="IStorageService"/> is wrapped by Storage's identity chokepoint; a supplied
/// <see cref="IDataService"/> replaces Data core and its realization, so composition refuses the host.
/// </summary>
public sealed class SegmentationCoverageSpec : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Koan-Tenancy", Guid.CreateVersion7().ToString("n"));
    private readonly IServiceProvider? _initialHost = AppHost.Current;

    public SegmentationCoverageSpec()
    {
        Directory.CreateDirectory(_root);
        TestHooks.ResetDataConfigs();
    }

    public void Dispose()
    {
        AppHost.Current = _initialHost;
        TestHooks.ResetDataConfigs();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task A_supplied_storage_service_is_isolated_per_tenant()
    {
        var supplied = new RecordingStorage();
        var services = Services();
        services.AddSingleton<IStorageService>(supplied);

        services.AddKoan();
        using var provider = services.BuildServiceProvider();
        AppHost.Current = provider;
        var storage = provider.GetRequiredService<IStorageService>();

        using (Tenant.Use("acme")) await storage.Put("p", "c", "report.pdf", Bytes("ACME"), "application/pdf");
        using (Tenant.Use("globex")) await storage.Put("p", "c", "report.pdf", Bytes("GLOBEX"), "application/pdf");

        supplied.Keys.Should().HaveCount(2, "each tenant writes its own physical key");
        using (Tenant.Use("globex")) (await Text(await storage.Read("p", "c", "report.pdf"))).Should().Be("GLOBEX");
        using (Tenant.Use("acme")) (await Text(await storage.Read("p", "c", "report.pdf"))).Should().Be("ACME");
        provider.GetServices<ISegmentationRealization>()
            .Select(static realization => realization.SegmentationRealization.PillarId)
            .Should().Contain("storage");
    }

    [Fact]
    public void A_supplied_data_service_without_the_Data_realization_refuses_composition()
    {
        var services = Services();
        services.AddSingleton<IDataService>(_ => throw new InvalidOperationException("never resolved"));

        var compose = () => services.AddKoan();

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*segmentation*'data'*IDataService*");
    }

    private ServiceCollection Services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Tenancy:Posture"] = "Closed",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(_root, "coverage.db")}",
                ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = "Test",
            ApplicationName = nameof(SegmentationCoverageSpec),
            ContentRootPath = _root,
        });
        return services;
    }

    private static Stream Bytes(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));

    private static async Task<string> Text(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>An application-owned storage service that keeps whatever physical key it is handed.</summary>
    private sealed class RecordingStorage : IStorageService
    {
        private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => _objects.Keys.ToArray();

        public async Task<StorageObject> Put(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            _objects[key] = buffer.ToArray();
            return new StorageObject { Id = key, Key = key, Name = key, ContentType = contentType, Size = buffer.Length, Container = container };
        }

        public Task<Stream> Read(string profile, string container, string key, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(_objects[key]));

        public Task<(Stream Stream, long? Length)> ReadRange(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> Delete(string profile, string container, string key, CancellationToken ct = default)
            => Task.FromResult(_objects.TryRemove(key, out _));

        public Task<bool> Exists(string profile, string container, string key, CancellationToken ct = default)
            => Task.FromResult(_objects.ContainsKey(key));

        public Task<ObjectStat?> Head(string profile, string container, string key, CancellationToken ct = default)
            => Task.FromResult<ObjectStat?>(null);

        public Task<StorageObject> TransferToProfile(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Uri> PresignRead(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Uri> PresignWrite(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StorageObjectInfo> ListObjects(string profile, string container, string? prefix = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
