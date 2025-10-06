using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Extensions;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Testing.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Cache.Adapter.Redis.IntegrationTests;

public sealed class RedisCacheAdapterTests : IClassFixture<RedisAutoFixture>, IAsyncLifetime
{
    private readonly RedisAutoFixture _fixture;
    private IHost? _hostOne;
    private IHost? _hostTwo;

    public RedisCacheAdapterTests(RedisAutoFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task RedisCache_RoundTrips_String_Value()
    {
        await EnsureHostsAsync();
        var client = _hostOne!.Services.GetRequiredService<ICacheClient>();
        var entry = client.CreateEntry<string>(new CacheKey($"tests:redis:roundtrip:{Guid.NewGuid():N}"))
            .WithAbsoluteTtl(TimeSpan.FromMinutes(1));

        await entry.SetAsync("payload", CancellationToken.None);
        var value = await entry.GetAsync(CancellationToken.None);

        value.Should().Be("payload");
    }

    [SkippableFact]
    public async Task RedisCache_EnumerateByTag_ReturnsStoredKey()
    {
        await EnsureHostsAsync();
        var key = new CacheKey($"tests:redis:tags:{Guid.NewGuid():N}");
        var entry = _hostOne!.Services.GetRequiredService<ICacheClient>()
            .CreateEntry<string>(key)
            .WithTags("alpha", "beta")
            .WithAbsoluteTtl(TimeSpan.FromMinutes(1));

        await entry.SetAsync("tagged-value", CancellationToken.None);

        var store = _hostOne.Services.GetRequiredService<ICacheStore>();
        var results = new List<TaggedCacheKey>();
        await foreach (var tagged in store.EnumerateByTagAsync("alpha", CancellationToken.None))
        {
            results.Add(tagged);
        }

        results.Should().ContainSingle(x => x.Key.Matches(key.Value));
    }

    [SkippableFact]
    public async Task RedisCache_PubSubInvalidation_RemovesKeyAcrossHosts()
    {
        await EnsureHostsAsync();
        var key = new CacheKey($"tests:redis:invalidation:{Guid.NewGuid():N}");
        var entry = _hostOne!.Services.GetRequiredService<ICacheClient>()
            .CreateEntry<string>(key)
            .WithTags("invalidate-test")
            .WithAbsoluteTtl(TimeSpan.FromMinutes(5));

        await entry.SetAsync("distributed-value", CancellationToken.None);

        var clientTwo = _hostTwo!.Services.GetRequiredService<ICacheClient>();
        var existing = await clientTwo.CreateEntry<string>(key)
            .GetAsync(CancellationToken.None);
        existing.Should().Be("distributed-value");

        var options = new CacheEntryOptions
        {
            Tags = new HashSet<string>(new[] { "invalidate-test" }, StringComparer.OrdinalIgnoreCase)
        };

        var store = _hostOne.Services.GetRequiredService<ICacheStore>();
        await store.PublishInvalidationAsync(key, options, CancellationToken.None);

        await WaitUntilAsync(async () =>
        {
            var refreshed = await clientTwo.CreateEntry<string>(key)
                .GetAsync(CancellationToken.None);
            return refreshed is null;
        }, TimeSpan.FromSeconds(5));
    }

    public async Task InitializeAsync()
    {
        await EnsureHostsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_hostTwo is not null)
        {
            await StopHostedServicesAsync(_hostTwo.Services);
            await _hostTwo.StopAsync();
            _hostTwo.Dispose();
        }

        if (_hostOne is not null)
        {
            await StopHostedServicesAsync(_hostOne.Services);
            await _hostOne.StopAsync();
            _hostOne.Dispose();
        }
    }

    private async Task EnsureHostsAsync()
    {
        Skip.If(string.IsNullOrWhiteSpace(_fixture.ConnectionString), "Redis connection not available for integration tests.");

        if (_hostOne is not null && _hostTwo is not null)
        {
            return;
        }

        _hostOne = await CreateHostAsync("host-a");
        _hostTwo = await CreateHostAsync("host-b");
    }

    private async Task<IHost> CreateHostAsync(string nodeName)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddDebug();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Provider"] = "redis",
                ["Cache:Redis:Configuration"] = _fixture.ConnectionString!,
                ["Cache:Redis:ChannelName"] = "koan-cache-tests",
                ["Cache:Redis:InstanceName"] = "tests",
                ["Cache:Redis:EnablePubSubInvalidation"] = "true",
                ["Cache:Redis:EnableStaleWhileRevalidate"] = "true"
            })
            .Build();

        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddKoanCache(configuration);
        builder.Services.AddKoanCacheAdapter("redis", configuration);

        var host = builder.Build();
        await StartHostedServicesAsync(host.Services);
        return host;
    }

    private static async Task StartHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    private static async Task StopHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException("Condition was not met before timeout elapsed.");
    }
}
