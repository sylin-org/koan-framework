using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Testing;
using Koan.Testing.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Mongo.Tests.Support;

internal sealed class MongoConnectorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IConfiguration _configuration;

    private MongoConnectorFixture(ServiceProvider provider, IDataService data, IConfiguration configuration, string database, string collectionPrefix)
    {
        _provider = provider;
        Data = data;
        _configuration = configuration;
        Database = database;
        CollectionPrefix = collectionPrefix;
    }

    public IServiceProvider Services => _provider;

    public IDataService Data { get; }

    public IConfiguration Configuration => _configuration;

    public string Database { get; }

    public string CollectionPrefix { get; }

    public static ValueTask<MongoConnectorFixture> CreateAsync(TestContext ctx)
    {
        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        var mongo = ctx.GetMongoFixture();
        if (!mongo.IsAvailable || string.IsNullOrWhiteSpace(mongo.ConnectionString))
        {
            throw new InvalidOperationException($"Mongo fixture is unavailable: {mongo.UnavailableReason ?? "unspecified"}");
        }

        var database = mongo.Database;
        if (string.IsNullOrWhiteSpace(database))
        {
            database = $"koan_{ctx.ExecutionId:N}";
        }

        var collectionPrefix = ctx.ExecutionId.ToString("N");

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "mongo",
            ["Koan:Data:Sources:Default:ConnectionString"] = mongo.ConnectionString,
            ["Koan:Data:Sources:Default:Database"] = database,
            ["Koan:Data:Sources:Default:Options:CollectionPrefix"] = collectionPrefix,
            ["Koan:Data:Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Koan:Data:Mongo:Database"] = database,
            ["Koan:Data:Mongo:CollectionPrefix"] = collectionPrefix
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKoan();

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
            // KoanEnv is sticky per-process; ignore duplicate initialization failures.
        }

        AppHost.Current = provider;
        var data = provider.GetRequiredService<IDataService>();

    return ValueTask.FromResult(new MongoConnectorFixture(provider, data, configuration, database, collectionPrefix));
    }

    public void BindHost()
    {
        AppHost.Current = _provider;
    }

    public string EnsurePartition(TestContext ctx)
    {
        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        const string Key = "mongo-connector-partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"mongo-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    public EntityPartitionLease LeasePartition(string partition)
        => new(partition);

    public async Task ResetAsync<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        BindHost();
        TestHooks.ResetDataConfigs();
        await Data.Execute<TEntity, TKey, int>(new Instruction(DataInstructions.Clear));
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

        _provider.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async ValueTask DisposeAsyncCore(IAsyncDisposable asyncDisposable)
    {
        try
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
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
            if (string.IsNullOrWhiteSpace(partition))
            {
                throw new ArgumentException("Partition must be provided.", nameof(partition));
            }

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
