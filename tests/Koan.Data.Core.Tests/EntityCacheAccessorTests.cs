using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Core.Tests;

public sealed class EntityCacheAccessorTests : IDisposable
{
    private readonly IServiceProvider _provider;
    private readonly TestCacheClient _cacheClient;

    public EntityCacheAccessorTests()
    {
        _cacheClient = new TestCacheClient();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheClient>(_cacheClient);
        services.AddSingleton<ICachePolicyRegistry>(new TestCachePolicyRegistry());
        _provider = services.BuildServiceProvider();
        AppHost.Current = _provider;
    }

    [Fact]
    public async Task Flush_UsesPolicyTagsAndAdditional()
    {
        await SampleEntity.Cache.Flush(new[] { "extra" }, CancellationToken.None);

        _cacheClient.LastFlushTags.Should().BeEquivalentTo("sample", "extra");
        _cacheClient.FlushCalls.Should().Be(1);
    }

    [Fact]
    public async Task Flush_StringOverload_IncludesAdditionalTag()
    {
        await SampleEntity.Cache.Flush("custom", CancellationToken.None);

        _cacheClient.LastFlushTags.Should().BeEquivalentTo("sample", "custom");
    }

    [Fact]
    public async Task Flush_IgnoresPlaceholderTagsFromPolicies()
    {
        var registry = (TestCachePolicyRegistry)_provider.GetRequiredService<ICachePolicyRegistry>();
        registry.AddPolicy(new CachePolicyDescriptor(
            CacheScope.Entity,
            "sample:{Id}",
            CacheStrategy.GetOrSet,
            CacheConsistencyMode.StaleWhileRevalidate,
            AbsoluteTtl: null,
            SlidingTtl: null,
            AllowStaleFor: null,
            ForcePublishInvalidation: false,
            Tags: new[] { "global", "sample:{Id}" },
            Region: null,
            ScopeId: null,
            Metadata: new Dictionary<string, string>(),
            TargetMember: null,
            DeclaringType: typeof(SampleEntity)));

        await SampleEntity.Cache.Flush(CancellationToken.None);

        _cacheClient.LastFlushTags.Should().BeEquivalentTo("sample", "global");
    }

    [Fact]
    public async Task Count_WithoutPolicies_ReturnsZeroWithoutCallingClient()
    {
        var registry = (TestCachePolicyRegistry)_provider.GetRequiredService<ICachePolicyRegistry>();
        registry.Clear();

        var result = await SampleEntity.Cache.Count(CancellationToken.None);

        result.Should().Be(0);
        _cacheClient.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Any_WithAdditionalTags_DeduplicatesInputs()
    {
        _cacheClient.CountResult = 1;

        var any = await SampleEntity.Cache.Any(new[] { "sample", "Alt", "alt " }, CancellationToken.None);

        any.Should().BeTrue();
        _cacheClient.LastCountTags.Should().BeEquivalentTo("sample", "Alt");
    }

    [Fact]
    public async Task Any_UsesCountAndReturnsBoolean()
    {
        _cacheClient.CountResult = 2;

        var any = await SampleEntity.Cache.Any(CancellationToken.None);

        any.Should().BeTrue();
        _cacheClient.CountCalls.Should().Be(1);
    }

    [Fact]
    public async Task Any_WithNoResolvedTags_ShortCircuits()
    {
        var registry = (TestCachePolicyRegistry)_provider.GetRequiredService<ICachePolicyRegistry>();
        registry.Clear();

        var any = await SampleEntity.Cache.Any(new[] { " ", "placeholder:{Id}" }, CancellationToken.None);

        any.Should().BeFalse();
        _cacheClient.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Flush_ThrowsWhenClientMissing()
    {
        var registry = (TestCachePolicyRegistry)_provider.GetRequiredService<ICachePolicyRegistry>();
        var original = AppHost.Current;
        var services = new ServiceCollection().AddSingleton<ICachePolicyRegistry>(registry).BuildServiceProvider();
        AppHost.Current = services;

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await SampleEntity.Cache.Flush(CancellationToken.None));
        }
        finally
        {
            AppHost.Current = original;
            (services as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task Flush_ThrowsWhenRegistryMissing()
    {
        var original = AppHost.Current;
        var services = new ServiceCollection().AddSingleton<ICacheClient>(_cacheClient).BuildServiceProvider();
        AppHost.Current = services;

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await SampleEntity.Cache.Flush(CancellationToken.None));
        }
        finally
        {
            AppHost.Current = original;
            (services as IDisposable)?.Dispose();
        }
    }

    public void Dispose()
    {
        AppHost.Current = null;
        (_provider as IDisposable)?.Dispose();
    }

    private sealed class SampleEntity : Entity<SampleEntity, Guid>
    {
    }

    private sealed class TestCacheClient : ICacheClient
    {
        public ICacheStore Store => throw new NotSupportedException();

        public int FlushCalls { get; private set; }
        public int CountCalls { get; private set; }
        public long CountResult { get; set; }
        public IReadOnlyCollection<string> LastFlushTags { get; private set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> LastCountTags { get; private set; } = Array.Empty<string>();

        public ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key) => throw new NotSupportedException();

        public CacheScopeHandle BeginScope(string scopeId, string? region = null) => throw new NotSupportedException();

        public ValueTask<long> FlushTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
        {
            FlushCalls++;
            LastFlushTags = tags;
            return ValueTask.FromResult((long)tags.Count);
        }

        public ValueTask<long> CountTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
        {
            CountCalls++;
            LastCountTags = tags;
            return ValueTask.FromResult(CountResult);
        }

        public ValueTask<CacheFetchResult> GetAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<bool> ExistsAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TestCachePolicyRegistry : ICachePolicyRegistry
    {
        private readonly List<CachePolicyDescriptor> _policies = new()
        {
            new CachePolicyDescriptor(
                CacheScope.Entity,
                "sample:{Id}",
                CacheStrategy.GetOrSet,
                CacheConsistencyMode.StaleWhileRevalidate,
                AbsoluteTtl: null,
                SlidingTtl: null,
                AllowStaleFor: null,
                ForcePublishInvalidation: false,
                Tags: new[] { "sample" },
                Region: null,
                ScopeId: null,
                Metadata: new Dictionary<string, string>(),
                TargetMember: null,
                DeclaringType: typeof(SampleEntity))
        };

        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(Type type)
            => type == typeof(SampleEntity) ? _policies : Array.Empty<CachePolicyDescriptor>();

        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(System.Reflection.MemberInfo member)
            => Array.Empty<CachePolicyDescriptor>();

        public IReadOnlyList<CachePolicyDescriptor> GetAllPolicies()
            => _policies;

        public bool TryGetPolicy(Type type, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            if (type == typeof(SampleEntity) && _policies.Count > 0)
            {
                descriptor = _policies[0];
                return true;
            }

            descriptor = null;
            return false;
        }

        public bool TryGetPolicy(System.Reflection.MemberInfo member, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            descriptor = null;
            return false;
        }

        public void Rebuild(IEnumerable<System.Reflection.Assembly> assemblies)
            => throw new NotSupportedException();

        public void Clear()
            => _policies.Clear();

        public void AddPolicy(CachePolicyDescriptor descriptor)
            => _policies.Add(descriptor);
    }
}
