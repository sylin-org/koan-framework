using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Connector.Redis;
using Koan.Testing;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Redis.Tests.Support;

internal sealed class RedisConnectorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;
    private readonly int _database;

    private RedisConnectorFixture(ServiceProvider provider, IDataService data, IConfiguration configuration, string connectionString, int database)
    {
        _provider = provider;
        Data = data;
        _configuration = configuration;
        _connectionString = connectionString;
        _database = database;
    }

    public IServiceProvider Services => _provider;

    public IDataService Data { get; }

    public IConfiguration Configuration => _configuration;

    public string ConnectionString => _connectionString;

    public int Database => _database;

    public static async ValueTask<RedisConnectorFixture> CreateAsync(TestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var redis = ctx.GetRedisFixture();
        if (!redis.IsAvailable || string.IsNullOrWhiteSpace(redis.ConnectionString))
        {
            throw new InvalidOperationException($"Redis fixture is unavailable: {redis.UnavailableReason ?? "unspecified"}");
        }

        var database = (Math.Abs(ctx.ExecutionId.GetHashCode()) % 14) + 1; // avoid db 0 collisions
        var connectionString = $"{redis.ConnectionString},defaultDatabase={database}";

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "redis",
            ["Koan:Data:Redis:ConnectionString"] = connectionString,
            ["Koan:Data:Redis:Database"] = database.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKoan();
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
        services.AddRedisAdapter();

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

        return new RedisConnectorFixture(provider, data, configuration, connectionString, database);
    }

    public void BindHost()
    {
        AppHost.Current = _provider;
    }

    public string EnsurePartition(TestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        const string Key = "redis-connector-partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"redis-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    public EntityPartitionLease LeasePartition(string partition) => new(partition);

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
