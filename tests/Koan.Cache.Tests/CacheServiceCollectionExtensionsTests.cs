using System;
using System.Collections.Generic;
using FluentAssertions;
using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Memory.Options;
using Koan.Cache.Adapter.Memory.Stores;
using Koan.Cache.Extensions;
using Koan.Cache.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class CacheServiceCollectionExtensionsTests
{
    [Fact]
    public void AddKoanCache_DefaultsProviderToMemoryWhenAdapterRegistered()
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
    }

    [Fact]
    public void AddKoanCache_ThrowsWhenAdapterMissing()
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
    }

    [Fact]
    public void AddKoanCacheAdapter_RegistersDescriptor()
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
    }
}

internal sealed class FakeMemoryAdapterRegistrar : ICacheAdapterRegistrar
{
    public string Name => "memory";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton<IOptions<MemoryCacheAdapterOptions>>(_ => Microsoft.Extensions.Options.Options.Create(new MemoryCacheAdapterOptions()));
        services.AddSingleton<ILogger<MemoryCacheStore>>(_ => NullLogger<MemoryCacheStore>.Instance);
        services.AddSingleton<ICacheStore, MemoryCacheStore>();
    }
}
