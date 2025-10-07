using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Options;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

using CacheFacade = Koan.Cache.Cache;

namespace Koan.Tests.Cache.Unit.Specs.Facade;

public sealed class CacheFacadeSpec
{
    private readonly ITestOutputHelper _output;

    public CacheFacadeSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Exists_delegates_to_client()
        => SpecWithClient(nameof(Exists_delegates_to_client), async client =>
        {
            client.ExistsResult = true;
            using var cts = new CancellationTokenSource();

            var result = await CacheFacade.Exists("alpha", cts.Token);

            result.Should().BeTrue();
            client.ExistsCalls.Should().Be(1);
            client.LastExistsKey.Should().Be(new CacheKey("alpha"));
            client.LastExistsOptions.Should().NotBeNull();
            client.LastExistsToken.Should().Be(cts.Token);
        });

    [Fact]
    public Task Exists_when_client_missing_throws()
        => Spec(nameof(Exists_when_client_missing_throws), async () =>
        {
            var backup = AppHost.Current;
            AppHost.Current = null;

            try
            {
                var act = async () => await CacheFacade.Exists("missing");
                await act.Should().ThrowAsync<InvalidOperationException>();
            }
            finally
            {
                AppHost.Current = backup;
            }
        });

    [Fact]
    public Task Tags_with_null_enumerable_short_circuits()
        => SpecWithClient(nameof(Tags_with_null_enumerable_short_circuits), async client =>
        {
            var tagSet = CacheFacade.Tags((IEnumerable<string>?)null!);

            var flushed = await tagSet.Flush();
            var counted = await tagSet.Count();
            var any = await tagSet.Any();

            flushed.Should().Be(0);
            counted.Should().Be(0);
            any.Should().BeFalse();
            client.FlushCalls.Should().Be(0);
            client.CountCalls.Should().Be(0);
        });

    [Fact]
    public Task Tags_normalizes_and_deduplicates_before_flush()
        => SpecWithClient(nameof(Tags_normalizes_and_deduplicates_before_flush), async client =>
        {
            client.FlushResult = 2;

            var removed = await CacheFacade.Tags("  Foo  ", "foo", "bar", "bar ", "baz", string.Empty).Flush();

            removed.Should().Be(2);
            client.FlushCalls.Should().Be(1);
            client.LastFlushTags.Should().NotBeNull();
            client.LastFlushTags.Should().BeEquivalentTo("Foo", "bar", "baz");
        });

    [Fact]
    public Task Tags_any_uses_count_when_tags_present()
        => SpecWithClient(nameof(Tags_any_uses_count_when_tags_present), async client =>
        {
            client.CountResult = 3;

            var any = await CacheFacade.Tags("tenant", "feature").Any();

            any.Should().BeTrue();
            client.CountCalls.Should().Be(1);
            client.LastCountTags.Should().BeEquivalentTo("tenant", "feature");
        });

    [Fact]
    public Task Tags_any_without_tags_short_circuits()
        => SpecWithClient(nameof(Tags_any_without_tags_short_circuits), async client =>
        {
            var any = await CacheFacade.Tags().Any();

            any.Should().BeFalse();
            client.CountCalls.Should().Be(0);
            client.FlushCalls.Should().Be(0);
        });

    private Task Spec(string scenario, Func<Task> body)
        => TestPipeline.For<CacheFacadeSpec>(_output, scenario)
            .Assert(async _ => await body().ConfigureAwait(false))
            .RunAsync();

    private Task SpecWithClient(string scenario, Func<FakeCacheClient, Task> body)
        => TestPipeline.For<CacheFacadeSpec>(_output, scenario)
            .Assert(async _ =>
            {
                var services = new ServiceCollection();
                var client = new FakeCacheClient();
                services.AddSingleton<ICacheClient>(client);
                using var provider = services.BuildServiceProvider();

                var previous = AppHost.Current;
                AppHost.Current = provider;

                try
                {
                    await body(client).ConfigureAwait(false);
                }
                finally
                {
                    AppHost.Current = previous;
                }
            })
            .RunAsync();

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
