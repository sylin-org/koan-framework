using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Cache.Tests;

[Collection("CacheAppHost")]
public sealed class CacheFacadeTests : IDisposable
{
    private readonly IServiceProvider _provider;
    private readonly FakeCacheClient _client;
    private readonly IServiceProvider? _previousProvider;

    public CacheFacadeTests()
    {
        _client = new FakeCacheClient();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheClient>(_client);
        _provider = services.BuildServiceProvider();

        _previousProvider = AppHost.Current;
        AppHost.Current = _provider;
    }

    [Fact]
    public async Task Exists_DelegatesToClient()
    {
        _client.ExistsResult = true;
        using var cts = new CancellationTokenSource();

        var result = await Cache.Exists("alpha", cts.Token);

        result.Should().BeTrue();
        _client.ExistsCalls.Should().Be(1);
        _client.LastExistsKey.Should().Be(new CacheKey("alpha"));
        _client.LastExistsOptions.Should().NotBeNull();
        _client.LastExistsToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Exists_WhenClientMissing_Throws()
    {
        var backup = AppHost.Current;
        AppHost.Current = null;

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Cache.Exists("missing"));
        }
        finally
        {
            AppHost.Current = backup;
        }
    }

    [Fact]
    public async Task Tags_WithNullEnumerable_ShortCircuitsOperations()
    {
        var tagSet = Cache.Tags((IEnumerable<string>?)null!);

        var flushed = await tagSet.Flush();
        var counted = await tagSet.Count();
        var any = await tagSet.Any();

        flushed.Should().Be(0);
        counted.Should().Be(0);
        any.Should().BeFalse();
        _client.FlushCalls.Should().Be(0);
        _client.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Tags_NormalizesAndDeduplicatesBeforeFlush()
    {
        _client.FlushResult = 2;

        var removed = await Cache.Tags("  Foo  ", "foo", "bar", "bar ", "baz", string.Empty).Flush();

        removed.Should().Be(2);
        _client.FlushCalls.Should().Be(1);
        _client.LastFlushTags.Should().NotBeNull();
        _client.LastFlushTags.Should().BeEquivalentTo("Foo", "bar", "baz");
    }

    [Fact]
    public async Task Tags_AnyUsesCountWhenTagsPresent()
    {
        _client.CountResult = 3;

        var any = await Cache.Tags("tenant", "feature").Any();

        any.Should().BeTrue();
        _client.CountCalls.Should().Be(1);
        _client.LastCountTags.Should().BeEquivalentTo("tenant", "feature");
    }

    [Fact]
    public async Task Tags_AnyWithNoTags_ShortCircuits()
    {
        var any = await Cache.Tags().Any();

        any.Should().BeFalse();
        _client.CountCalls.Should().Be(0);
    }

    public void Dispose()
    {
        AppHost.Current = _previousProvider;
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed class FakeCacheClient : ICacheClient
    {
        public int ExistsCalls { get; private set; }
        public CacheKey? LastExistsKey { get; private set; }
        public CacheEntryOptions? LastExistsOptions { get; private set; }
        public CancellationToken LastExistsToken { get; private set; }
        public bool ExistsResult { get; set; }

        public int FlushCalls { get; private set; }
        public IReadOnlyCollection<string>? LastFlushTags { get; private set; }
        public CancellationToken LastFlushToken { get; private set; }
        public long FlushResult { get; set; }

        public int CountCalls { get; private set; }
        public IReadOnlyCollection<string>? LastCountTags { get; private set; }
        public CancellationToken LastCountToken { get; private set; }
        public long CountResult { get; set; }

        public ICacheStore Store => throw new NotSupportedException();

        public ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key)
            => throw new NotSupportedException();

        public CacheScopeHandle BeginScope(string scopeId, string? region = null)
            => new(scopeId, region, null);

        public ValueTask<long> FlushTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
        {
            FlushCalls++;
            LastFlushTags = tags;
            LastFlushToken = ct;
            return ValueTask.FromResult(FlushResult);
        }

        public ValueTask<long> CountTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
        {
            CountCalls++;
            LastCountTags = tags;
            LastCountToken = ct;
            return ValueTask.FromResult(CountResult);
        }

        public ValueTask<CacheFetchResult> GetAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<bool> ExistsAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        {
            ExistsCalls++;
            LastExistsKey = key;
            LastExistsOptions = options;
            LastExistsToken = ct;
            return ValueTask.FromResult(ExistsResult);
        }

        public ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
