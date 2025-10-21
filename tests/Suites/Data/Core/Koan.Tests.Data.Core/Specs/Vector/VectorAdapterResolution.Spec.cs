using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        await TestPipeline.For<VectorAdapterResolutionSpec>(_output, nameof(Uses_vector_attribute_before_defaults))
            .UsingServiceProvider(key: "services", configure: static (_, services) =>
            {
                services.AddKoanDataCore();
                services.AddKoanDataVector();
                services.AddSingleton<IDataService, DataService>();
                services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("foo"));
                services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("bar"));
                services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("json"));
            })
            .Assert(ctx =>
            {
                var provider = ctx.GetRequiredItem<ServiceProviderFixture>("services").Services;
                var vector = provider.GetRequiredService<IVectorService>();
                var repo = vector.TryGetRepository<EntityWithVectorAdapter, string>();
                repo.Should().NotBeNull();
                (repo as FakeVectorRepo<EntityWithVectorAdapter, string>)!.ProviderName.Should().Be("foo");
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Uses_default_provider_when_attribute_missing()
    {
        await TestPipeline.For<VectorAdapterResolutionSpec>(_output, nameof(Uses_default_provider_when_attribute_missing))
            .UsingServiceProvider(key: "services", configure: static (_, services) =>
            {
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
            })
            .Assert(ctx =>
            {
                var provider = ctx.GetRequiredItem<ServiceProviderFixture>("services").Services;
                var vector = provider.GetRequiredService<IVectorService>();
                var repo = vector.TryGetRepository<EntityWithSourceOnly, string>();
                repo.Should().NotBeNull();
                (repo as FakeVectorRepo<EntityWithSourceOnly, string>)!.ProviderName.Should().Be("bar");
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Falls_back_to_source_provider_when_no_defaults()
    {
        await TestPipeline.For<VectorAdapterResolutionSpec>(_output, nameof(Falls_back_to_source_provider_when_no_defaults))
            .UsingServiceProvider(key: "services", configure: static (_, services) =>
            {
                services.AddKoanDataCore();
                services.AddKoanDataVector();
                services.AddSingleton<IDataService, DataService>();
                services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("foo"));
                services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("bar"));
                services.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("json"));
            })
            .Assert(ctx =>
            {
                var provider = ctx.GetRequiredItem<ServiceProviderFixture>("services").Services;
                var vector = provider.GetRequiredService<IVectorService>();
                var repo = vector.TryGetRepository<EntityWithSourceOnly, string>();
                repo.Should().NotBeNull();
                (repo as FakeVectorRepo<EntityWithSourceOnly, string>)!.ProviderName.Should().Be("json");
                return ValueTask.CompletedTask;
            })
            .RunAsync();
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

        public Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
            => Task.FromResult(new VectorQueryResult<TKey>(Array.Empty<VectorMatch<TKey>>(), null));
    }

    private sealed class FakeVectorFactory : IVectorAdapterFactory
    {
        private readonly string _provider;

        public FakeVectorFactory(string provider)
        {
            _provider = provider;
        }

        public bool CanHandle(string candidate) => string.Equals(candidate, _provider, StringComparison.OrdinalIgnoreCase);

        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
            => new FakeVectorRepo<TEntity, TKey>(_provider);
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