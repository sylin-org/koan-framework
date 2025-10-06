using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Serialization;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Decorators;
using Koan.Cache.Diagnostics;
using Koan.Cache.Options;
using Koan.Cache.Scope;
using Koan.Cache.Singleflight;
using Koan.Cache.Stores;
using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class CacheRepositoryDecoratorTests
{
    [Fact]
    public void TryDecorate_WithEntityPolicy_WrapsRepository()
    {
    using var context = CacheTestContext.Create();
    var registry = CreateRegistry();

        var decorator = new CacheRepositoryDecorator(registry, NullLogger<CacheRepositoryDecorator>.Instance);
        var repository = new TestRepository();

        var decorated = decorator.TryDecorate(typeof(TestEntity), typeof(Guid), repository, context.Services);

        decorated.Should().NotBeNull();
        decorated.Should().BeAssignableTo<IDataRepository<TestEntity, Guid>>();
    }

    [Fact]
    public async Task GetAsync_UsesCacheAfterFirstFetch()
    {
    using var context = CacheTestContext.Create();
    var registry = CreateRegistry();

        var decorator = new CacheRepositoryDecorator(registry, NullLogger<CacheRepositoryDecorator>.Instance);
        var repository = new TestRepository();
        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "alpha" });

        var decorated = decorator.TryDecorate(typeof(TestEntity), typeof(Guid), repository, context.Services);
        var cached = Assert.IsAssignableFrom<IDataRepository<TestEntity, Guid>>(decorated);

        var first = await cached.GetAsync(id);
        var second = await cached.GetAsync(id);

        repository.GetCalls.Should().Be(1, "second invocation should read from cache");
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Name.Should().Be("alpha");
        context.Store.StoredValues.ContainsKey($"test:{id}").Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAndDelete_MaintainCacheEntries()
    {
    using var context = CacheTestContext.Create();
    var registry = CreateRegistry();

        var decorator = new CacheRepositoryDecorator(registry, NullLogger<CacheRepositoryDecorator>.Instance);
        var repository = new TestRepository();

        var decorated = decorator.TryDecorate(typeof(TestEntity), typeof(Guid), repository, context.Services);
        var cached = Assert.IsAssignableFrom<IDataRepository<TestEntity, Guid>>(decorated);

        var entity = new TestEntity { Name = "beta" };
        var saved = await cached.UpsertAsync(entity);

        saved.Id.Should().NotBe(Guid.Empty);
        context.Store.StoredValues.ContainsKey($"test:{saved.Id}").Should().BeTrue();

        var deleted = await cached.DeleteAsync(saved.Id);
        deleted.Should().BeTrue();
        context.Store.StoredValues.ContainsKey($"test:{saved.Id}").Should().BeFalse();
    }

    private static ICachePolicyRegistry CreateRegistry()
    {
        var descriptor = new CachePolicyDescriptor(
            CacheScope.Entity,
            "test:{Id}",
            CacheStrategy.GetOrSet,
            CacheConsistencyMode.StaleWhileRevalidate,
            AbsoluteTtl: null,
            SlidingTtl: null,
            AllowStaleFor: null,
            ForcePublishInvalidation: false,
            Tags: Array.Empty<string>(),
            Region: null,
            ScopeId: null,
            Metadata: new Dictionary<string, string>(),
            TargetMember: null,
            DeclaringType: typeof(TestEntity));

        return new StubPolicyRegistry(descriptor);
    }

    private sealed class TestEntity : IEntity<Guid>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestRepository : IDataRepository<TestEntity, Guid>
    {
        private readonly Dictionary<Guid, TestEntity> _entries = new();

        public int GetCalls { get; private set; }

        public void Seed(TestEntity entity)
        {
            _entries[entity.Id] = Clone(entity);
        }

        public Task<TestEntity?> GetAsync(Guid id, CancellationToken ct = default)
        {
            GetCalls++;
            _entries.TryGetValue(id, out var entity);
            return Task.FromResult(entity is null ? null : Clone(entity));
        }

        public Task<IReadOnlyList<TestEntity>> QueryAsync(object? query, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CountResult> CountAsync(CountRequest<TestEntity> request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TestEntity> UpsertAsync(TestEntity model, CancellationToken ct = default)
        {
            if (model.Id == Guid.Empty)
            {
                model.Id = Guid.NewGuid();
            }

            _entries[model.Id] = Clone(model);
            return Task.FromResult(Clone(model));
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var removed = _entries.Remove(id);
            return Task.FromResult(removed);
        }

        public Task<int> UpsertManyAsync(IEnumerable<TestEntity> models, CancellationToken ct = default)
        {
            var count = 0;
            foreach (var model in models)
            {
                if (model.Id == Guid.Empty)
                {
                    model.Id = Guid.NewGuid();
                }

                _entries[model.Id] = Clone(model);
                count++;
            }

            return Task.FromResult(count);
        }

        public Task<int> DeleteManyAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            var count = 0;
            foreach (var id in ids)
            {
                if (_entries.Remove(id))
                {
                    count++;
                }
            }

            return Task.FromResult(count);
        }

        public Task<int> DeleteAllAsync(CancellationToken ct = default)
        {
            var count = _entries.Count;
            _entries.Clear();
            return Task.FromResult(count);
        }

        public Task<long> RemoveAllAsync(RemoveStrategy strategy, CancellationToken ct = default)
        {
            var count = _entries.Count;
            _entries.Clear();
            return Task.FromResult((long)count);
        }

        public IBatchSet<TestEntity, Guid> CreateBatch()
            => throw new NotSupportedException();

        private static TestEntity Clone(TestEntity entity)
            => new() { Id = entity.Id, Name = entity.Name };
    }

    private sealed class CacheTestContext : IDisposable
    {
        private readonly CacheInstrumentation _instrumentation;

        private CacheTestContext(CacheClient client, TestCacheStore store, CacheInstrumentation instrumentation, ServiceProvider services)
        {
            Client = client;
            Store = store;
            _instrumentation = instrumentation;
            Services = services;
        }

        public CacheClient Client { get; }
        public TestCacheStore Store { get; }
        public ServiceProvider Services { get; }

        public static CacheTestContext Create()
        {
            var store = new TestCacheStore();
            var serializers = new ICacheSerializer[]
            {
                new TestStringSerializer(),
                new TestJsonSerializer()
            };

            var instrumentation = new CacheInstrumentation(NullLogger<CacheInstrumentation>.Instance);
            var client = new CacheClient(
                store,
                serializers,
                new CacheSingleflightRegistry(),
                new CacheScopeAccessor(),
                instrumentation,
                new TestOptionsMonitor<CacheOptions>(new CacheOptions()),
                NullLogger<CacheClient>.Instance);

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ICacheClient>(client)
                .AddSingleton<ICacheReader>(client)
                .AddSingleton<ICacheWriter>(client)
                .AddSingleton<ICacheStore>(store)
                .BuildServiceProvider();

            return new CacheTestContext(client, store, instrumentation, services);
        }

        public void Dispose()
        {
            _instrumentation.Dispose();
            Services.Dispose();
        }
    }

    private sealed class TestCacheStore : ICacheStore
    {
        private readonly ConcurrentDictionary<string, (CacheValue Value, CacheEntryOptions Options)> _entries = new(StringComparer.Ordinal);

        public string ProviderName => "test";

        public CacheCapabilities Capabilities { get; } = new(
            SupportsBinary: true,
            SupportsPubSubInvalidation: true,
            SupportsCompareExchange: false,
            SupportsRegionScoping: true,
            Hints: new HashSet<string>());

        public ConcurrentDictionary<string, (CacheValue Value, CacheEntryOptions Options)> StoredValues => _entries;

        public ValueTask<CacheFetchResult> FetchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        {
            if (_entries.TryGetValue(key.Value, out var entry))
            {
                return ValueTask.FromResult(CacheFetchResult.HitResult(entry.Value, entry.Options, null, null));
            }

            return ValueTask.FromResult(CacheFetchResult.Miss(options));
        }

        public ValueTask SetAsync(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct)
        {
            _entries[key.Value] = (value, options);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct)
        {
            var removed = _entries.TryRemove(key.Value, out _);
            return ValueTask.FromResult(removed);
        }

        public ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishInvalidationAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        {
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTagAsync(string tag, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var kvp in _entries)
            {
                if (kvp.Value.Options.Tags.Contains(tag))
                {
                    yield return new TaggedCacheKey(tag, new CacheKey(kvp.Key), null);
                }
            }

            await Task.CompletedTask;
        }

        public ValueTask<bool> ExistsAsync(CacheKey key, CancellationToken ct)
        {
            var exists = _entries.ContainsKey(key.Value);
            return ValueTask.FromResult(exists);
        }
    }

    private sealed class TestStringSerializer : ICacheSerializer
    {
        public string ContentType => CacheConstants.ContentTypes.String;

        public bool CanHandle(Type type) => type == typeof(string);

        public ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct)
            => ValueTask.FromResult(CacheValue.FromString(value?.ToString() ?? string.Empty));

        public ValueTask<CacheValue> SerializeAsync(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct)
            => ValueTask.FromResult(CacheValue.FromString(value?.ToString() ?? string.Empty));

        public ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct)
            => ValueTask.FromResult((T?)(object?)value.ToText());

        public ValueTask<object?> DeserializeAsync(CacheValue value, Type returnType, CancellationToken ct)
            => ValueTask.FromResult((object?)value.ToText());
    }

    private sealed class TestJsonSerializer : ICacheSerializer
    {
        public string ContentType => CacheConstants.ContentTypes.Json;

        public bool CanHandle(Type type) => true;

        public ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(value);
            return ValueTask.FromResult(CacheValue.FromJson(json));
        }

        public ValueTask<CacheValue> SerializeAsync(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(value, runtimeType, settings: null);
            return ValueTask.FromResult(CacheValue.FromJson(json));
        }

        public ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct)
        {
            var json = value.ToText();
            if (string.IsNullOrEmpty(json))
            {
                return ValueTask.FromResult(default(T?));
            }

            var result = JsonConvert.DeserializeObject<T>(json);
            return ValueTask.FromResult(result);
        }

        public ValueTask<object?> DeserializeAsync(CacheValue value, Type returnType, CancellationToken ct)
        {
            var json = value.ToText();
            if (string.IsNullOrEmpty(json))
            {
                return ValueTask.FromResult<object?>(null);
            }

            var result = JsonConvert.DeserializeObject(json, returnType);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubPolicyRegistry : ICachePolicyRegistry
    {
        private readonly CachePolicyDescriptor _descriptor;
        private readonly IReadOnlyList<CachePolicyDescriptor> _policies;

        public StubPolicyRegistry(CachePolicyDescriptor descriptor)
        {
            _descriptor = descriptor;
            _policies = new[] { descriptor };
        }

        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(Type type)
            => type == typeof(TestEntity) ? _policies : Array.Empty<CachePolicyDescriptor>();

        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(MemberInfo member)
            => Array.Empty<CachePolicyDescriptor>();

        public IReadOnlyList<CachePolicyDescriptor> GetAllPolicies()
            => _policies;

    public bool TryGetPolicy(Type type, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            if (type == typeof(TestEntity))
            {
                descriptor = _descriptor;
                return true;
            }

            descriptor = null;
            return false;
        }

    public bool TryGetPolicy(MemberInfo member, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            descriptor = null;
            return false;
        }
    }
}
