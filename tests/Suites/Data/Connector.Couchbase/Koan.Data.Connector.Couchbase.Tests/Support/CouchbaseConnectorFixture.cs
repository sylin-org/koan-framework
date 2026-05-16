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
using Koan.Testing.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Couchbase.Tests.Support;

internal sealed class CouchbaseConnectorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IConfiguration _configuration;

    private CouchbaseConnectorFixture(ServiceProvider provider, IDataService data, IConfiguration configuration, string bucket)
    {
        _provider = provider;
        Data = data;
        _configuration = configuration;
        Bucket = bucket;
    }

    public IServiceProvider Services => _provider;

    public IDataService Data { get; }

    public IConfiguration Configuration => _configuration;

    public string Bucket { get; }

    public static ValueTask<CouchbaseConnectorFixture> Create(TestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var couchbase = ctx.GetRequiredItem<CouchbaseContainerFixture>("couchbase");
        if (!couchbase.IsAvailable || string.IsNullOrWhiteSpace(couchbase.ConnectionString))
        {
            throw new InvalidOperationException($"Couchbase fixture is unavailable: {couchbase.UnavailableReason ?? "unspecified"}");
        }

        var bucket = couchbase.Bucket;

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "couchbase",
            ["Koan:Data:Sources:Default:ConnectionString"] = couchbase.ConnectionString,
            ["Koan:Data:Sources:Default:Database"] = bucket,
            ["Koan:Data:Couchbase:ConnectionString"] = couchbase.ConnectionString,
            ["Koan:Data:Couchbase:Bucket"] = bucket,
            ["Koan:Data:Couchbase:Username"] = couchbase.Username ?? "Administrator",
            ["Koan:Data:Couchbase:Password"] = couchbase.Password ?? "password"
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

        return ValueTask.FromResult(new CouchbaseConnectorFixture(provider, data, configuration, bucket));
    }

    public void BindHost()
    {
        AppHost.Current = _provider;
    }

    public string EnsurePartition(TestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        const string Key = "couchbase-connector-partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"couchbase-{ctx.ExecutionId:n}";
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
