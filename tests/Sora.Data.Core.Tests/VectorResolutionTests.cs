using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;
using Xunit;

namespace Sora.Data.Core.Tests;

public class VectorResolutionTests
{
    // Fake vector repo and factory used to assert selection
    private sealed class FakeVectorRepo<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        public string ProviderName { get; }
        public FakeVectorRepo(string name) => ProviderName = name;
        public Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default) => Task.FromResult(0);
        public Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
            => Task.FromResult(new VectorQueryResult<TKey>(Array.Empty<VectorMatch<TKey>>(), null));
    }

    private sealed class FakeVectorFactory(string provider) : IVectorAdapterFactory
    {
        public bool CanHandle(string provider) => string.Equals(provider, Provider, StringComparison.OrdinalIgnoreCase);
        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp) where TEntity : class, IEntity<TKey> where TKey : notnull
            => new FakeVectorRepo<TEntity, TKey>(Provider);
        public string Provider { get; } = provider;
    }

    [SourceAdapter("json")]
    private sealed class A : IEntity<string> { [Identifier] public string Id { get; set; } = string.Empty; }

    [VectorAdapter("foo")]
    [SourceAdapter("json")]
    private sealed class B : IEntity<string> { [Identifier] public string Id { get; set; } = string.Empty; }

    private static ServiceProvider BuildServices(IConfiguration? cfg = null)
    {
        var sc = new ServiceCollection();
        if (cfg != null) sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddSingleton<IDataService, DataService>();
        // Register vector factories: foo, bar, and json
        sc.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("foo"));
        sc.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("bar"));
        sc.AddSingleton<IVectorAdapterFactory>(new FakeVectorFactory("json"));
        return sc.BuildServiceProvider();
    }

    [Fact]
    public void Uses_VectorAdapter_attribute_first()
    {
        var sp = BuildServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.TryGetVectorRepository<B, string>();
        repo.Should().NotBeNull();
        var name = (repo as FakeVectorRepo<B, string>)!.ProviderName;
        name.Should().Be("foo");
    }

    [Fact]
    public void Uses_DefaultProvider_when_no_attribute()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("Sora:Data:VectorDefaults:DefaultProvider", "bar") }).Build();
        var sp = BuildServices(cfg);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.TryGetVectorRepository<A, string>();
        repo.Should().NotBeNull();
        var name = (repo as FakeVectorRepo<A, string>)!.ProviderName;
        name.Should().Be("bar");
    }

    [Fact]
    public void Falls_back_to_source_provider_when_no_defaults()
    {
        var sp = BuildServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.TryGetVectorRepository<A, string>();
        repo.Should().NotBeNull();
        var name = (repo as FakeVectorRepo<A, string>)!.ProviderName;
        name.Should().Be("json");
    }
}