using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Extensions;
using Koan.Cache.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Extensions;

public sealed class CacheServiceCollectionExtensionsSpec
{
    private readonly ITestOutputHelper _output;

    public CacheServiceCollectionExtensionsSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task AddKoanCache_defaults_provider_to_memory_when_adapter_registered()
        => Spec(nameof(AddKoanCache_defaults_provider_to_memory_when_adapter_registered), () =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKoanCache(configuration);
            services.AddSingleton<ICacheAdapterRegistrar, TestMemoryAdapterRegistrar>();
            services.AddKoanCacheAdapter("memory", configuration);

            using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<ICacheStore>();
            store.ProviderName.Should().Be("memory");

            var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            options.Provider.Should().Be("memory");
        });

    [Fact]
    public Task AddKoanCache_throws_when_adapter_missing()
        => Spec(nameof(AddKoanCache_throws_when_adapter_missing), () =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKoanCache(configuration);

            using var provider = services.BuildServiceProvider();
            Func<ICacheStore> resolve = () => provider.GetRequiredService<ICacheStore>();

            resolve.Should().Throw<InvalidOperationException>()
                .WithMessage("*No cache adapter has been registered*");
        });

    [Fact]
    public Task AddKoanCacheAdapter_registers_descriptor()
        => Spec(nameof(AddKoanCacheAdapter_registers_descriptor), () =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKoanCache(configuration);
            services.AddSingleton<ICacheAdapterRegistrar, TestMemoryAdapterRegistrar>();
            services.AddKoanCacheAdapter("memory", configuration);

            using var provider = services.BuildServiceProvider();
            provider.GetServices<CacheAdapterDescriptor>()
                .Should().Contain(descriptor => descriptor.Name == "memory");
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CacheServiceCollectionExtensionsSpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
}

internal sealed class TestMemoryAdapterRegistrar : ICacheAdapterRegistrar
{
    public string Name => "memory";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICacheStore, TestMemoryCacheStore>();
    }
}

internal sealed class TestMemoryCacheStore : ICacheStore
{
    public string ProviderName => "memory";

    public CacheCapabilities Capabilities { get; } = CacheCapabilities.None;

    public ValueTask<CacheFetchResult> FetchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask SetAsync(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask PublishInvalidationAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        => throw new NotSupportedException();

    public IAsyncEnumerable<TaggedCacheKey> EnumerateByTagAsync(string tag, CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask<bool> ExistsAsync(CacheKey key, CancellationToken ct)
        => throw new NotSupportedException();
}
