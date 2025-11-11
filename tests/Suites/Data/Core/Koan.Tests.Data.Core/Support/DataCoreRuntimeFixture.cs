using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Runtime;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Json;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core.Transactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading;

namespace Koan.Tests.Data.Core.Support;

internal sealed class DataCoreRuntimeFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _rootPath;
    private readonly string? _sqlitePath;

    private DataCoreRuntimeFixture(ServiceProvider provider, string rootPath, string? sqlitePath)
    {
        _provider = provider;
        _rootPath = rootPath;
        _sqlitePath = sqlitePath;
    }

    public IServiceProvider Services => _provider;

    public string RootPath => _rootPath;

    public string? SqlitePath => _sqlitePath;

    public static ValueTask<DataCoreRuntimeFixture> CreateAsync(TestContext ctx, bool includeSqlite = false)
    {
        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        var root = Path.Combine(Path.GetTempPath(), "Koan-DataCore", ctx.ExecutionId.ToString("n"));
        Directory.CreateDirectory(root);

        string? sqlitePath = null;

        var configurationValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Koan:Data:Json:DirectoryPath"] = root
        };

        if (includeSqlite)
        {
            sqlitePath = Path.Combine(root, "data.sqlite");
            configurationValues["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={sqlitePath}";
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKoan();
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddJsonAdapter(o => o.DirectoryPath = root);
        services.AddKoanTransactions();
        if (includeSqlite)
        {
            services.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={sqlitePath}");
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        try
        {
            KoanEnv.TryInitialize(provider);
        }
        catch
        {
            // Best effort – KoanEnv is sticky per process
        }

        AppHost.Current = provider;

        var runtime = provider.GetService<IAppRuntime>();
        runtime?.Discover();
        runtime?.Start();

        return ValueTask.FromResult(new DataCoreRuntimeFixture(provider, root, sqlitePath));
    }

    public EntityPartitionLease UsePartition(string? name = null)
        => new(name ?? $"partition-{Guid.CreateVersion7():n}");

    public void BindHost()
    {
        AppHost.Current = _provider;
    }

    public void ResetEntityCaches()
    {
        BindHost();
        TestHooks.ResetDataConfigs();
    }

    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(AppHost.Current, _provider))
        {
            AppHost.Current = null;
        }
        TestHooks.ResetDataConfigs();

        if (_provider is IAsyncDisposable asyncDisposable)
        {
            return DisposeAsyncCore(asyncDisposable);
        }

        (_provider as IDisposable)?.Dispose();
        CleanupArtifacts();
        return ValueTask.CompletedTask;
    }

    private async ValueTask DisposeAsyncCore(IAsyncDisposable asyncDisposable)
    {
        try
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            CleanupArtifacts();
        }
    }

    private void CleanupArtifacts()
    {
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

    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
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
