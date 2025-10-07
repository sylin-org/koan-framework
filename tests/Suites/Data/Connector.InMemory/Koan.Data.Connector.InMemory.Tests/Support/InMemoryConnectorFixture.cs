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

namespace Koan.Data.Connector.InMemory.Tests.Support;

internal sealed class InMemoryConnectorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IConfiguration _configuration;

    private InMemoryConnectorFixture(ServiceProvider provider, IDataService data, IConfiguration configuration)
    {
        _provider = provider;
        Data = data;
        _configuration = configuration;
    }

    public IServiceProvider Services => _provider;

    public IDataService Data { get; }

    public IConfiguration Configuration => _configuration;

    public static ValueTask<InMemoryConnectorFixture> CreateAsync(TestContext ctx)
    {
        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://default"
            })
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

        return ValueTask.FromResult(new InMemoryConnectorFixture(provider, data, configuration));
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

        const string Key = "inmemory-connector-partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"inmemory-{ctx.ExecutionId:n}";
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
        await Data.Execute<TEntity, TKey, int>(new Instruction("data.clear"));
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
