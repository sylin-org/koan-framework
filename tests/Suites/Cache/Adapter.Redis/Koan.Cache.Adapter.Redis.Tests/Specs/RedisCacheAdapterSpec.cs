using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Adapter.Redis;
using Koan.Cache.Extensions;
using Xunit.Abstractions;

namespace Koan.Cache.Adapter.Redis.Tests.Specs;

public sealed class RedisCacheAdapterSpec
{
    private readonly ITestOutputHelper _output;

    public RedisCacheAdapterSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Set_and_fetch_roundtrip_across_clients()
        => TestPipeline.For<RedisCacheAdapterSpec>(_output, nameof(Set_and_fetch_roundtrip_across_clients))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var fixture = ctx.GetRedisFixture();
                if (!fixture.IsAvailable || string.IsNullOrWhiteSpace(fixture.ConnectionString))
                {
                    throw new InvalidOperationException($"Redis unavailable: {fixture.UnavailableReason ?? "unspecified"}");
                }

                var token = ctx.ExecutionId.ToString("N");
                var keyPrefix = $"cache:{token}:";
                var tagPrefix = $"cache:tag:{token}:";
                var channel = $"koan-cache-{token}";

                await using var providerA = BuildServiceProvider(fixture.ConnectionString!, keyPrefix, tagPrefix, channel);
                await using var providerB = BuildServiceProvider(fixture.ConnectionString!, keyPrefix, tagPrefix, channel);
                await using var hostedA = await StartHostedServicesAsync(providerA, ctx.Cancellation).ConfigureAwait(false);
                await using var hostedB = await StartHostedServicesAsync(providerB, ctx.Cancellation).ConfigureAwait(false);

                var clientA = providerA.GetRequiredService<ICacheClient>();
                var clientB = providerB.GetRequiredService<ICacheClient>();

                var key = new CacheKey($"redis-roundtrip-{token}");
                var writer = clientA.CreateEntry<string>(key)
                    .WithAbsoluteTtl(TimeSpan.FromMinutes(5))
                    .WithTags("redis-integration", token);

                await writer.SetAsync("payload", ctx.Cancellation).ConfigureAwait(false);

                var reader = clientB.CreateEntry<string>(key);
                var value = await reader.GetAsync(ctx.Cancellation).ConfigureAwait(false);
                value.Should().Be("payload");

                var tagCount = await clientB.CountTagsAsync(new[] { "redis-integration" }, ctx.Cancellation).ConfigureAwait(false);
                tagCount.Should().Be(1);

                var removed = await clientB.FlushTagsAsync(new[] { "redis-integration" }, ctx.Cancellation).ConfigureAwait(false);
                removed.Should().Be(1);

                var afterFlush = await reader.GetAsync(ctx.Cancellation).ConfigureAwait(false);
                afterFlush.Should().BeNull();
            })
            .RunAsync();

    [Fact]
    public Task Stale_entries_expire_after_allowance()
        => TestPipeline.For<RedisCacheAdapterSpec>(_output, nameof(Stale_entries_expire_after_allowance))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var fixture = ctx.GetRedisFixture();
                if (!fixture.IsAvailable || string.IsNullOrWhiteSpace(fixture.ConnectionString))
                {
                    throw new InvalidOperationException($"Redis unavailable: {fixture.UnavailableReason ?? "unspecified"}");
                }

                var token = ctx.ExecutionId.ToString("N");
                var keyPrefix = $"cache:{token}:";
                var tagPrefix = $"cache:tag:{token}:";
                var channel = $"koan-cache-{token}-stale";

                await using var provider = BuildServiceProvider(fixture.ConnectionString!, keyPrefix, tagPrefix, channel);
                await using var hosted = await StartHostedServicesAsync(provider, ctx.Cancellation).ConfigureAwait(false);

                var client = provider.GetRequiredService<ICacheClient>();

                var key = new CacheKey($"redis-stale-{token}");
                var entry = client.CreateEntry<string>(key)
                    .WithAbsoluteTtl(TimeSpan.FromMilliseconds(150))
                    .AllowStaleFor(TimeSpan.FromMilliseconds(200));

                await entry.SetAsync("payload", ctx.Cancellation).ConfigureAwait(false);

                await Task.Delay(TimeSpan.FromMilliseconds(175), ctx.Cancellation).ConfigureAwait(false);
                var stale = await entry.GetAsync(ctx.Cancellation).ConfigureAwait(false);
                stale.Should().Be("payload");

                await Task.Delay(TimeSpan.FromMilliseconds(250), ctx.Cancellation).ConfigureAwait(false);
                var final = await entry.GetAsync(ctx.Cancellation).ConfigureAwait(false);
                final.Should().BeNull();

                var exists = await entry.Exists(ctx.Cancellation).ConfigureAwait(false);
                exists.Should().BeFalse();
            })
            .RunAsync();

    private static ServiceProvider BuildServiceProvider(string connectionString, string keyPrefix, string tagPrefix, string channelName)
    {
        var settings = new Dictionary<string, string?>
        {
            [CacheConstants.Configuration.ProviderKey] = "redis",
            [CacheConstants.Configuration.Redis.Configuration] = connectionString,
            [CacheConstants.Configuration.Redis.KeyPrefix] = keyPrefix,
            [CacheConstants.Configuration.Redis.TagPrefix] = tagPrefix,
            [CacheConstants.Configuration.Redis.ChannelName] = channelName
        };

        var configuration = new ConfigurationManager();
        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

    _ = typeof(RedisCacheAdapterRegistrar);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKoanCache(configuration);
        services.AddKoanCacheAdapter("redis", configuration);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ICachePolicyRegistry>();

        return provider;
    }

    private static async ValueTask<IAsyncDisposable> StartHostedServicesAsync(ServiceProvider provider, CancellationToken cancellation)
    {
        var hosted = provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hosted)
        {
            await service.StartAsync(cancellation).ConfigureAwait(false);
        }

        return new HostedServicesScope(hosted);
    }

    private sealed class HostedServicesScope : IAsyncDisposable
    {
        private readonly IHostedService[] _services;

        public HostedServicesScope(IHostedService[] services)
        {
            _services = services;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var service in _services.Reverse())
            {
                try
                {
                    await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignored for teardown
                }
            }
        }
    }
}
