using System.Collections.Concurrent;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Serialization;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Diagnostics;
using Koan.Cache.Options;
using Koan.Cache.Scope;
using Koan.Cache.Singleflight;
using Koan.Cache.Stores;
using Koan.Tests.Cache.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Client;

public sealed class CacheClientSpec
{
    private readonly ITestOutputHelper _output;

    public CacheClientSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task CreateEntry_applies_current_scope()
        => Spec(nameof(CreateEntry_applies_current_scope), () =>
        {
            using var context = CacheClientContext.Create();
            using var scope = context.Client.BeginScope("tenant-1", "region-x");

            var builder = context.Client.CreateEntry<string>(new CacheKey("scoped"));

            builder.Options.ScopeId.Should().Be("tenant-1");
            builder.Options.Region.Should().Be("region-x");
        });

    [Fact]
    public Task SetAsync_null_value_removes_entry()
        => SpecAsync(nameof(SetAsync_null_value_removes_entry), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("null-removal");

            await context.Store.SetAsync(key, CacheValue.FromString("existing"), new CacheEntryOptions(), CancellationToken.None);

            var entry = context.Client.CreateEntry<string>(key);
            await entry.SetAsync(null!, CancellationToken.None);

            context.Store.Removes.Should().Be(1);
            var fetch = await context.Store.FetchAsync(key, new CacheEntryOptions(), CancellationToken.None);
            fetch.Hit.Should().BeFalse();
        });

    [Fact]
    public Task SetAsync_force_publish_triggers_invalidation()
        => SpecAsync(nameof(SetAsync_force_publish_triggers_invalidation), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("publish-on-set");

            var entry = context.Client.CreateEntry<string>(key)
                .PublishInvalidation();

            await entry.SetAsync("payload", CancellationToken.None);

            context.Store.PublishCount.Should().Be(1);
        });

    [Fact]
    public Task SetAsync_string_content_uses_string_serializer()
        => SpecAsync(nameof(SetAsync_string_content_uses_string_serializer), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("string-content");

            var entry = context.Client.CreateEntry<string>(key)
                .WithContentKind(CacheContentKind.String);

            await entry.SetAsync("payload", CancellationToken.None);

            context.StringSerializer.SerializeCalls.Should().Be(1);
            context.JsonSerializer.SerializeCalls.Should().Be(0);
        });

    [Fact]
    public Task TouchAsync_existing_entry_uses_scoped_options()
        => SpecAsync(nameof(TouchAsync_existing_entry_uses_scoped_options), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("touch-existing");

            await context.Store.SetAsync(key, CacheValue.FromString("value"), new CacheEntryOptions(), CancellationToken.None);

            using (context.Client.BeginScope("tenant-touch", "region-west"))
            {
                await context.Client.TouchAsync(key, new CacheEntryOptions(), CancellationToken.None);
            }

            context.Store.Touches.Should().Be(1);
            context.Store.TouchedKeys.Should().Contain(key.Value);
            context.Store.LastTouchedOptions.Should().NotBeNull();
            context.Store.LastTouchedOptions!.ScopeId.Should().Be("tenant-touch");
            context.Store.LastTouchedOptions.Region.Should().Be("region-west");
        });

    [Fact]
    public Task TouchAsync_missing_entry_is_noop()
        => SpecAsync(nameof(TouchAsync_missing_entry_is_noop), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("touch-missing");

            var act = () => context.Client.TouchAsync(key, new CacheEntryOptions(), CancellationToken.None).AsTask();

            await act.Should().NotThrowAsync();

            context.Store.Touches.Should().Be(0);
            context.Store.MissingTouches.Should().Be(1);
        });

    [Fact]
    public Task BeginScope_dispose_restores_previous_scope()
        => Spec(nameof(BeginScope_dispose_restores_previous_scope), () =>
        {
            using var context = CacheClientContext.Create();

            var initial = context.Client.CreateEntry<string>(new CacheKey("pre"));
            initial.Options.ScopeId.Should().BeNull();

            using (context.Client.BeginScope("tenant-scope", "region-east"))
            {
                var scoped = context.Client.CreateEntry<string>(new CacheKey("scoped"));
                scoped.Options.ScopeId.Should().Be("tenant-scope");
                scoped.Options.Region.Should().Be("region-east");
            }

            var after = context.Client.CreateEntry<string>(new CacheKey("post"));
            after.Options.ScopeId.Should().BeNull();
        });

    [Fact]
    public Task GetOrAddAsync_deduplicates_concurrent_work()
        => SpecAsync(nameof(GetOrAddAsync_deduplicates_concurrent_work), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("singleflight");
            var callCount = 0;

            async ValueTask<string?> Factory(CancellationToken ct)
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(25, ct);
                return "computed";
            }

            var builder = context.Client.CreateEntry<string>(key)
                .WithAbsoluteTtl(TimeSpan.FromMinutes(1));

            var tasks = Enumerable.Range(0, 5)
                .Select(_ => builder.GetOrAddAsync(Factory, CancellationToken.None).AsTask())
                .ToArray();

            var results = await Task.WhenAll(tasks);
            results.Should().AllBe("computed");
            callCount.Should().Be(1);
            context.Store.StoredValues.Should().ContainKey(key.Value);
        });

    [Fact]
    public Task GetAsync_deserializes_stored_value()
        => SpecAsync(nameof(GetAsync_deserializes_stored_value), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("deserialize-test");

            await context.Store.SetAsync(key, CacheValue.FromString("{\"Value\":5}"), new CacheEntryOptions(), CancellationToken.None);

            var builder = context.Client.CreateEntry<TestPayload>(key);
            var result = await builder.GetAsync(CancellationToken.None);

            result.Should().NotBeNull();
            result!.Value.Should().Be(5);
        });

    [Fact]
    public Task Exists_when_present_returns_true()
        => SpecAsync(nameof(Exists_when_present_returns_true), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("exists-present");

            var entry = context.Client.CreateEntry<string>(key);
            await entry.SetAsync("value", CancellationToken.None);

            var exists = await entry.Exists(CancellationToken.None);
            exists.Should().BeTrue();
        });

    [Fact]
    public Task Exists_when_missing_returns_false()
        => SpecAsync(nameof(Exists_when_missing_returns_false), async () =>
        {
            using var context = CacheClientContext.Create();
            var key = new CacheKey("exists-missing");

            var entry = context.Client.CreateEntry<string>(key);
            var exists = await entry.Exists(CancellationToken.None);

            exists.Should().BeFalse();
        });

    [Fact]
    public Task FlushTagsAsync_removes_tagged_entries()
        => SpecAsync(nameof(FlushTagsAsync_removes_tagged_entries), async () =>
        {
            using var context = CacheClientContext.Create();
            var first = context.Client.CreateEntry<string>(new CacheKey("tagged-1")).WithTags("todos");
            var second = context.Client.CreateEntry<string>(new CacheKey("tagged-2")).WithTags("todos", "open");

            await first.SetAsync("one", CancellationToken.None);
            await second.SetAsync("two", CancellationToken.None);

            var removed = await context.Client.FlushTagsAsync(new[] { "todos" }, CancellationToken.None);

            removed.Should().Be(2);
            context.Store.StoredValues.Should().BeEmpty();
        });

    [Fact]
    public Task CountTagsAsync_deduplicates_keys_across_tags()
        => SpecAsync(nameof(CountTagsAsync_deduplicates_keys_across_tags), async () =>
        {
            using var context = CacheClientContext.Create();
            var first = context.Client.CreateEntry<string>(new CacheKey("count-1")).WithTags("todos", "open");
            var second = context.Client.CreateEntry<string>(new CacheKey("count-2")).WithTags("todos");

            await first.SetAsync("one", CancellationToken.None);
            await second.SetAsync("two", CancellationToken.None);

            var count = await context.Client.CountTagsAsync(new[] { "todos", "open" }, CancellationToken.None);

            count.Should().Be(2);
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CacheClientSpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    private Task SpecAsync(string scenario, Func<Task> body)
        => TestPipeline.For<CacheClientSpec>(_output, scenario)
            .Assert(async _ =>
            {
                await body().ConfigureAwait(false);
            })
            .RunAsync();

    private sealed record TestPayload(int Value);

    private sealed class CacheClientContext : IDisposable
    {
        private CacheClientContext(CacheClient client, TestCacheStore store, TestStringSerializer stringSerializer, TestJsonSerializer jsonSerializer, CacheInstrumentation instrumentation)
        {
            Client = client;
            Store = store;
            StringSerializer = stringSerializer;
            JsonSerializer = jsonSerializer;
            _instrumentation = instrumentation;
        }

        public CacheClient Client { get; }
        public TestCacheStore Store { get; }
        public TestStringSerializer StringSerializer { get; }
        public TestJsonSerializer JsonSerializer { get; }
        private readonly CacheInstrumentation _instrumentation;

        public void Dispose()
        {
            _instrumentation.Dispose();
        }

        public static CacheClientContext Create(CacheOptions? options = null)
        {
            var store = new TestCacheStore();
            var stringSerializer = new TestStringSerializer();
            var jsonSerializer = new TestJsonSerializer();
            var instrumentation = new CacheInstrumentation(NullLogger<CacheInstrumentation>.Instance);
            var client = new CacheClient(
                store,
                new ICacheSerializer[] { stringSerializer, jsonSerializer },
                new CacheSingleflightRegistry(),
                new CacheScopeAccessor(),
                instrumentation,
                new TestOptionsMonitor<CacheOptions>(options ?? new CacheOptions()),
                NullLogger<CacheClient>.Instance);

            return new CacheClientContext(client, store, stringSerializer, jsonSerializer, instrumentation);
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

        public int PublishCount { get; private set; }
        public int Removes { get; private set; }

        public IReadOnlyDictionary<string, (CacheValue Value, CacheEntryOptions Options)> StoredValues => _entries;
        public int Touches { get; private set; }
        public int MissingTouches { get; private set; }
        public List<string> TouchedKeys { get; } = new();
        public CacheEntryOptions? LastTouchedOptions { get; private set; }

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
            Removes++;
            return ValueTask.FromResult(removed);
        }

        public ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        {
            if (_entries.ContainsKey(key.Value))
            {
                Touches++;
                LastTouchedOptions = options;
                TouchedKeys.Add(key.Value);
            }
            else
            {
                MissingTouches++;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask PublishInvalidationAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
        {
            PublishCount++;
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
        public int SerializeCalls { get; private set; }
        public string ContentType => CacheConstants.ContentTypes.String;

        public bool CanHandle(Type type) => type == typeof(string);

        public ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct)
        {
            SerializeCalls++;
            return ValueTask.FromResult(CacheValue.FromString(value?.ToString() ?? string.Empty));
        }

        public ValueTask<CacheValue> SerializeAsync(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct)
        {
            SerializeCalls++;
            return ValueTask.FromResult(CacheValue.FromString(value?.ToString() ?? string.Empty));
        }

        public ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct)
        {
            var text = value.ToText();
            return ValueTask.FromResult((T?)(object?)text);
        }

        public ValueTask<object?> DeserializeAsync(CacheValue value, Type returnType, CancellationToken ct)
        {
            return ValueTask.FromResult((object?)value.ToText());
        }
    }

    private sealed class TestJsonSerializer : ICacheSerializer
    {
        public int SerializeCalls { get; private set; }
        public string ContentType => CacheConstants.ContentTypes.Json;

        public bool CanHandle(Type type) => true;

        public ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct)
        {
            SerializeCalls++;
            var payload = value is null ? string.Empty : Newtonsoft.Json.JsonConvert.SerializeObject(value);
            return ValueTask.FromResult(CacheValue.FromString(payload));
        }

        public ValueTask<CacheValue> SerializeAsync(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct)
        {
            SerializeCalls++;
            var payload = value is null ? string.Empty : Newtonsoft.Json.JsonConvert.SerializeObject(value);
            return ValueTask.FromResult(CacheValue.FromString(payload));
        }

        public ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct)
        {
            var payload = value.ToText();
            return ValueTask.FromResult(string.IsNullOrWhiteSpace(payload)
                ? default
                : Newtonsoft.Json.JsonConvert.DeserializeObject<T>(payload));
        }

        public ValueTask<object?> DeserializeAsync(CacheValue value, Type returnType, CancellationToken ct)
        {
            var payload = value.ToText();
            return ValueTask.FromResult(string.IsNullOrWhiteSpace(payload)
                ? null
                : Newtonsoft.Json.JsonConvert.DeserializeObject(payload, returnType));
        }
    }
}
