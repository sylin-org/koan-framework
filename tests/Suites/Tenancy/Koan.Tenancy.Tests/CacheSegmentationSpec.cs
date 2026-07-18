using AwesomeAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core.Semantics.Segmentation;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

public sealed class CacheSegmentationSpec
{
    private static IReadOnlyDictionary<string, string?> Closed()
        => new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" };

    [Fact]
    public async Task Generic_keys_singleflight_and_tags_are_tenant_local()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Closed());
        var cache = runtime.Services.GetRequiredService<ICacheClient>();
        var key = new CacheKey("orders:dashboard");
        var options = new CacheEntryOptions().WithTags("orders");
        var acmeFactoryCalls = 0;
        var globexFactoryCalls = 0;

        using (Tenant.Use("acme"))
        {
            var value = await cache.GetOrAddAsync(
                key,
                _ =>
                {
                    acmeFactoryCalls++;
                    return ValueTask.FromResult<string?>("acme-value");
                },
                options,
                default);
            value.Should().Be("acme-value");
            (await cache.CountTags(["orders"], default)).Should().Be(1);
        }

        using (Tenant.Use("globex"))
        {
            (await cache.GetAsync<string>(key, options, default)).Should().BeNull();
            var value = await cache.GetOrAddAsync(
                key,
                _ =>
                {
                    globexFactoryCalls++;
                    return ValueTask.FromResult<string?>("globex-value");
                },
                options,
                default);
            value.Should().Be("globex-value");
            (await cache.CountTags(["orders"], default)).Should().Be(1);
        }

        acmeFactoryCalls.Should().Be(1);
        globexFactoryCalls.Should().Be(1);

        using (Tenant.Use("acme"))
        {
            (await cache.FlushTags(["orders"], default)).Should().Be(1);
            (await cache.GetAsync<string>(key, options, default)).Should().BeNull();
        }

        using (Tenant.Use("globex"))
            (await cache.GetAsync<string>(key, options, default)).Should().Be("globex-value");
    }

    [Fact]
    public async Task Generic_cache_refuses_a_missing_hard_segmentation_context()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Closed());
        var cache = runtime.Services.GetRequiredService<ICacheClient>();

        var act = async () => await cache.Exists(
            new CacheKey("orders:dashboard"),
            new CacheEntryOptions(),
            default);

        await act.Should().ThrowAsync<SegmentationRequiredException>()
            .WithMessage("*cache exists*requires isolation context 'tenant'*");
    }
}
