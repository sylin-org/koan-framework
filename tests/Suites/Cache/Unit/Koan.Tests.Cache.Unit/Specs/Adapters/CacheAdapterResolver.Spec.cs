using FluentAssertions;
using Koan.Cache.Adapters;
using Koan.Cache.Adapter.Memory;
using Koan.Cache.Adapter.Redis;
using Koan.Testing;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Adapters;

public sealed class CacheAdapterResolverSpec
{
    private readonly ITestOutputHelper _output;

    public CacheAdapterResolverSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Resolve_returns_memory_registrar_when_memory_adapter_referenced()
        => Spec(nameof(Resolve_returns_memory_registrar_when_memory_adapter_referenced), () =>
        {
            _ = typeof(MemoryCacheAdapterRegistrar);

            var registrar = CacheAdapterResolver.Resolve("memory");
            registrar.Should().BeOfType<MemoryCacheAdapterRegistrar>();
        });

    [Fact]
    public Task Resolve_returns_redis_registrar_when_redis_adapter_referenced()
        => Spec(nameof(Resolve_returns_redis_registrar_when_redis_adapter_referenced), () =>
        {
            _ = typeof(RedisCacheAdapterRegistrar);

            var registrar = CacheAdapterResolver.Resolve("redis");
            registrar.Should().BeOfType<RedisCacheAdapterRegistrar>();
        });

    [Fact]
    public Task Resolve_is_case_insensitive()
        => Spec(nameof(Resolve_is_case_insensitive), () =>
        {
            _ = typeof(MemoryCacheAdapterRegistrar);

            var registrar = CacheAdapterResolver.Resolve("MeMoRy");
            registrar.Should().BeOfType<MemoryCacheAdapterRegistrar>();
        });

    [Fact]
    public Task Resolve_throws_for_unknown_adapter()
        => Spec(nameof(Resolve_throws_for_unknown_adapter), () =>
        {
            Action resolve = () => CacheAdapterResolver.Resolve("unknown");

            resolve.Should().Throw<InvalidOperationException>()
                .WithMessage("*Cache adapter 'unknown' is not registered*");
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CacheAdapterResolverSpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
}
