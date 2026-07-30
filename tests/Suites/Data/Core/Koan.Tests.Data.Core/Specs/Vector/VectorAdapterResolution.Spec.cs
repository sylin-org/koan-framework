using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Core;
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
        Compose(services);
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
        Compose(services);
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
        Compose(services);
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
    public async Task Preferred_record_provider_hint_falls_through_to_vector_automatic_policy()
    {
        // Record-provider correlation is advisory for the distinct vector role. When the record provider has no vector
        // counterpart, the vector pillar may continue through its own automatic policy. Registered low-then-high so
        // DI order differs from the vector catalog's deterministic priority order.
        var services = NewServices();
        Compose(services);
        services.AddSingleton<IVectorAdapterFactory>(new LowPriorityVectorFactory());   // registered first
        services.AddSingleton<IVectorAdapterFactory>(new HighPriorityVectorFactory());  // higher [ProviderPriority]
        await using var sp = services.BuildServiceProvider();

        var repo = sp.GetRequiredService<IVectorService>().TryGetRepository<EntityWithUnmatchedSource, string>();
        repo.Should().NotBeNull();
        (((IDecoratedVectorRepository)repo!).InnerRepository as FakeVectorRepo<EntityWithUnmatchedSource, string>)!
            .ProviderName.Should().Be("high");
    }

    [Fact]
    public async Task Explicit_vector_attribute_never_falls_through_to_an_unrelated_provider()
    {
        var services = NewServices();
        Compose(services);
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("available"));
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IVectorService>()
            .TryGetRepository<EntityWithMissingVectorAdapter, string>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires vector provider 'missing'*will not substitute an unrelated provider*");
    }

    [Fact]
    public async Task Configured_vector_default_never_falls_through_to_an_unrelated_provider()
    {
        var services = NewServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:VectorDefaults:DefaultProvider"] = "missing"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        Compose(services);
        services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("available"));
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IVectorService>()
            .TryGetRepository<EntityWithSourceOnly, string>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires vector provider 'missing'*will not substitute an unrelated provider*");
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
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        return services;
    }

    private static void Compose(IServiceCollection services) => services.AddKoan(koan =>
    {
        var source = koan.Data.Source("Default");
        source.Vector<EntityWithUnmatchedSource>(space => space.Name("unmatched").Dimensions(1));
        source.Vector<EntityWithSourceOnly>(space => space.Name("source-only").Dimensions(1));
        source.Vector<EntityWithVectorAdapter>(space => space.Name("explicit").Dimensions(1));
        source.Vector<EntityWithMissingVectorAdapter>(space => space.Name("missing").Dimensions(1));
    });

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
    }

    private sealed class FakeVectorFactory : IVectorAdapterFactory
    {
        private readonly string _provider;

        public FakeVectorFactory(string provider)
        {
            _provider = provider;
        }

        public string Provider => _provider;

        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, VectorSpacePlan plan)
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
        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, VectorSpacePlan plan)
            where TEntity : class, IEntity<TKey> where TKey : notnull => new FakeVectorRepo<TEntity, TKey>("low");
        public Koan.Data.Abstractions.Naming.StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => new() { Style = Koan.Data.Abstractions.Naming.StorageNamingStyle.EntityType, PartitionSeparator = '#' };
    }

    [ProviderPriority(50)]
    private sealed class HighPriorityVectorFactory : IVectorAdapterFactory
    {
        public string Provider => "high";
        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, VectorSpacePlan plan)
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

    [VectorAdapter("missing")]
    private sealed class EntityWithMissingVectorAdapter : Entity<EntityWithMissingVectorAdapter, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
    }
}
