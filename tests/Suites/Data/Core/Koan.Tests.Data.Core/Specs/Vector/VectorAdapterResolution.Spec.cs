using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Tests.Data.Core.Specs.Vector;

public sealed class VectorAdapterResolutionSpec
{
    private readonly ITestOutputHelper _output;

    public VectorAdapterResolutionSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Uses_vector_attribute_before_defaults()
    {
        var services = NewServices();
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("foo"));
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("bar"));
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("json"));
        await using var sp = services.BuildServiceProvider();

        var vector = sp.GetRequiredService<IVectorService>();
        var repo = vector.TryGetRepository<EntityWithVectorAdapter, string>();
        repo.Should().NotBeNull();
        // TryGetRepository wraps the adapter in the data-axis isolation decorator (GAP C 0.3); unwrap to inspect the
        // selected provider.
        (((IDecoratedVectorRepository)repo!).InnerRepository as FakeVectorRepo<EntityWithVectorAdapter, string>)!
            .ProviderName.Should().Be("foo");
    }

    [Fact]
    public async Task Uses_default_provider_when_attribute_missing()
    {
        var services = NewServices();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:VectorDefaults:DefaultProvider"] = "bar"
            })
            .Build();

        services.AddSingleton<IConfiguration>(cfg);
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("foo"));
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("bar"));
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("json"));
        await using var sp = services.BuildServiceProvider();

        var vector = sp.GetRequiredService<IVectorService>();
        var repo = vector.TryGetRepository<EntityWithSourceOnly, string>();
        repo.Should().NotBeNull();
        (((IDecoratedVectorRepository)repo!).InnerRepository as FakeVectorRepo<EntityWithSourceOnly, string>)!
            .ProviderName.Should().Be("bar");
    }

    [Fact]
    public async Task Falls_back_to_source_provider_when_no_defaults()
    {
        var services = NewServices();
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("foo"));
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("bar"));
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("json"));
        await using var sp = services.BuildServiceProvider();

        var vector = sp.GetRequiredService<IVectorService>();
        var repo = vector.TryGetRepository<EntityWithSourceOnly, string>();
        repo.Should().NotBeNull();
        (((IDecoratedVectorRepository)repo!).InnerRepository as FakeVectorRepo<EntityWithSourceOnly, string>)!
            .ProviderName.Should().Be("json");
    }

    [Fact]
    public async Task Default_fallback_picks_highest_priority_when_desired_matches_no_vector_factory()
    {
        // ARCH-0103 §4.1 — the vector factory pick now uses the shared [ProviderPriority]+CanHandle ranking
        // (FactoryResolver), converging onto the same rule the record plane already used. When the desired provider
        // matches NO registered vector factory, the fallback resolves the HIGHEST-PRIORITY adapter, NOT the
        // first-registered. Registered low-then-high so DI order != priority order; the entity's [SourceAdapter] names a
        // provider no vector factory handles, forcing the no-match fallback. (Pre-ARCH-0103 this returned "low".)
        var services = NewServices();
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton<IVectorAdapterFactory>(new LowPriorityVectorFactory());   // registered first
        services.AddSingleton<IVectorAdapterFactory>(new HighPriorityVectorFactory());  // higher [ProviderPriority]
        await using var sp = services.BuildServiceProvider();

        var repo = sp.GetRequiredService<IVectorService>().TryGetRepository<EntityWithUnmatchedSource, string>();
        repo.Should().NotBeNull();
        (((IDecoratedVectorRepository)repo!).InnerRepository as FakeVectorRepo<EntityWithUnmatchedSource, string>)!
            .ProviderName.Should().Be("high");
    }

    // Mirrors the bespoke ServiceProviderFixture base wiring (logging + a no-op application lifetime)
    // that previously sat under .UsingServiceProvider(...), so the inlined provider matches it exactly.
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();
        return services;
    }

    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class FakeVectorRepo<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        public FakeVectorRepo(string providerName)
        {
            ProviderName = providerName;
        }

        public string ProviderName { get; }

        public Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<bool> Delete(TKey id, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
            => Task.FromResult(new VectorQueryResult<TKey>(Array.Empty<VectorMatch<TKey>>(), null));
    }

    private sealed class FakeVectorFactory : IVectorAdapterFactory
    {
        private readonly string _provider;

        public FakeVectorFactory(string provider)
        {
            _provider = provider;
        }

        public string Provider => _provider;

        public bool CanHandle(string candidate) => string.Equals(candidate, _provider, StringComparison.OrdinalIgnoreCase);

        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
            => new FakeVectorRepo<TEntity, TKey>(_provider);

        public Koan.Data.Abstractions.Naming.StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => new()
            {
                Style = Koan.Data.Abstractions.Naming.StorageNamingStyle.EntityType,
                PartitionSeparator = '#',
            };
    }

    // Two DISTINCT factory types with DISTINCT [ProviderPriority] for the priority-ranked-fallback regression
    // (ARCH-0103 §4.1). Registered low-then-high so DI order disagrees with priority order.
    [ProviderPriority(5)]
    private sealed class LowPriorityVectorFactory : IVectorAdapterFactory
    {
        public string Provider => "low";
        public bool CanHandle(string candidate) => string.Equals(candidate, "low", StringComparison.OrdinalIgnoreCase);
        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey> where TKey : notnull => new FakeVectorRepo<TEntity, TKey>("low");
        public Koan.Data.Abstractions.Naming.StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => new() { Style = Koan.Data.Abstractions.Naming.StorageNamingStyle.EntityType, PartitionSeparator = '#' };
    }

    [ProviderPriority(50)]
    private sealed class HighPriorityVectorFactory : IVectorAdapterFactory
    {
        public string Provider => "high";
        public bool CanHandle(string candidate) => string.Equals(candidate, "high", StringComparison.OrdinalIgnoreCase);
        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey> where TKey : notnull => new FakeVectorRepo<TEntity, TKey>("high");
        public Koan.Data.Abstractions.Naming.StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => new() { Style = Koan.Data.Abstractions.Naming.StorageNamingStyle.EntityType, PartitionSeparator = '#' };
    }

    [SourceAdapter("nomatch")]
    private sealed class EntityWithUnmatchedSource : Entity<EntityWithUnmatchedSource, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
    }

    [SourceAdapter("json")]
    private sealed class EntityWithSourceOnly : Entity<EntityWithSourceOnly, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
    }

    [VectorAdapter("foo")]
    [SourceAdapter("json")]
    private sealed class EntityWithVectorAdapter : Entity<EntityWithVectorAdapter, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
    }
}
