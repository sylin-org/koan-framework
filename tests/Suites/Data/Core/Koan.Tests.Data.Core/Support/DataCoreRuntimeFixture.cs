using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Transactions;
using Koan.Data.Vector;
using Koan.Testing.Integration;
using Koan.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Support;

internal sealed class DataCoreRuntimeFixture : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private readonly string _rootPath;
    private readonly string? _sqlitePath;
    private readonly FakeVectorService _vectorService;

    private DataCoreRuntimeFixture(IntegrationHost host, string rootPath, string? sqlitePath, FakeVectorService vectorService)
    {
        _host = host;
        _rootPath = rootPath;
        _sqlitePath = sqlitePath;
        _vectorService = vectorService;
    }

    public IServiceProvider Services => _host.Services;

    public string RootPath => _rootPath;

    public string? SqlitePath => _sqlitePath;

    public FakeVectorService VectorService => _vectorService;

    public static async Task<DataCoreRuntimeFixture> CreateAsync(bool includeSqlite = false, IReadOnlyDictionary<string, string?>? extraSettings = null, Action<IServiceCollection>? configureServices = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "Koan-DataCore", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);

        string? sqlitePath = null;

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Json:DirectoryPath"] = root
        };

        if (includeSqlite)
        {
            sqlitePath = Path.Combine(root, "data.sqlite");
            settings["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={sqlitePath}";
        }

        if (extraSettings is not null)
        {
            foreach (var kv in extraSettings)
                settings[kv.Key] = kv.Value;
        }

        var vectorService = new FakeVectorService();

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s =>
            {
                s.AddKoan(() => configureServices?.Invoke(s));
                s.AddKoanTransactions();
                // The deliberately non-isolating fake (inert unless a source names "fake-noniso") — the fail-closed
                // safety-net counter-example now that every real KV adapter announces isolation (ARCH-0103).
                s.AddSingleton<IDataAdapterFactory, NonIsolatingFakeAdapterFactory>();
                // FakeVectorService registered AFTER AddKoan() so it wins.
                s.AddSingleton<IVectorService>(vectorService);
                // Spec hooks run inside AddKoan's host composition so they can declare both DI contributors and
                // Entity lifecycle behavior without process-static test resets.
            })
            .StartAsync()
            .ConfigureAwait(false);

        AppHost.Current = host.Services;

        return new DataCoreRuntimeFixture(host, root, sqlitePath, vectorService);
    }

    public EntityPartitionLease UsePartition(string? name = null)
        => new(name ?? $"partition-{Guid.CreateVersion7():n}");

    public void BindHost()
    {
        AppHost.Current = _host.Services;
    }

    public void ResetEntityCaches()
    {
        BindHost();
        TestHooks.ResetDataConfigs();
    }

    public async ValueTask DisposeAsync()
    {
        if (ReferenceEquals(AppHost.Current, _host.Services))
        {
            AppHost.Current = null;
        }

        TestHooks.ResetDataConfigs();

        await _host.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    internal readonly struct EntityPartitionLease : IAsyncDisposable, IDisposable
    {
        private readonly IDisposable _lease;
        public EntityPartitionLease(string partition)
        {
            Partition = partition;
            _lease = EntityContext.Partition(partition);
        }

        public string Partition { get; }

        public void Dispose() => _lease.Dispose();

        public ValueTask DisposeAsync()
        {
            _lease.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
