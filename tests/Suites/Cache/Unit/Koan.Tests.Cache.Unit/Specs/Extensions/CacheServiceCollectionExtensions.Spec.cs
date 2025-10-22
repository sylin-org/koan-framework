using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Memory.Stores;
using Koan.Cache.Extensions;
using Koan.Cache.Options;
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
            services.AddKoanCacheAdapter("memory", configuration);

            using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<ICacheStore>();
            store.Should().BeOfType<MemoryCacheStore>();

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
