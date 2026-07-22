using AwesomeAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Cache.AdapterSurface.TestKit;

/// <summary>
/// Provider-neutral Cache contract. Concrete family cells own composition and infrastructure and
/// return one started lifetime; this base proves only behavior shared by every Cache store.
/// </summary>
public abstract class CacheAdapterConformanceSpecs : IAsyncLifetime
{
    private IAsyncDisposable? _lifetime;
    private ICacheClient? _client;

    protected abstract string Provider { get; }

    protected abstract Task<(IServiceProvider Services, IAsyncDisposable Lifetime)> StartHostAsync(
        CancellationToken cancellationToken);

    public async ValueTask InitializeAsync()
    {
        var started = await StartHostAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        _lifetime = started.Lifetime;
        _client = started.Services.GetRequiredService<ICacheClient>();
        started.Services.GetServices<ICacheStore>()
            .Should().Contain(store => string.Equals(store.Name, Provider, StringComparison.OrdinalIgnoreCase),
                $"Cache provider '{Provider}' must activate through the concrete cell's normal composition path");
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is not null) await _lifetime.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Set_then_get_returns_the_value()
    {
        var entry = Client.CreateEntry<string>(Key("set-get")).WithAbsoluteTtl(TimeSpan.FromMinutes(5));
        await entry.Set("hello", Ct);
        (await entry.Get(Ct)).Should().Be("hello");
    }

    [Fact]
    public async Task Get_on_missing_key_returns_default()
    {
        (await Client.CreateEntry<string>(Key("missing")).Get(Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Exists_tracks_set_and_remove()
    {
        var entry = Client.CreateEntry<string>(Key("exists")).WithAbsoluteTtl(TimeSpan.FromMinutes(5));
        (await entry.Exists(Ct)).Should().BeFalse();
        await entry.Set("value", Ct);
        (await entry.Exists(Ct)).Should().BeTrue();
        await entry.Remove(Ct);
        (await entry.Exists(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Set_overwrites_the_previous_value()
    {
        var entry = Client.CreateEntry<string>(Key("overwrite")).WithAbsoluteTtl(TimeSpan.FromMinutes(5));
        await entry.Set("first", Ct);
        await entry.Set("second", Ct);
        (await entry.Get(Ct)).Should().Be("second");
    }

    [Fact]
    public async Task Different_keys_remain_independent()
    {
        var first = Client.CreateEntry<string>(Key("first")).WithAbsoluteTtl(TimeSpan.FromMinutes(5));
        var second = Client.CreateEntry<string>(Key("second")).WithAbsoluteTtl(TimeSpan.FromMinutes(5));
        await first.Set("alpha", Ct);
        await second.Set("bravo", Ct);
        await first.Remove(Ct);
        (await second.Get(Ct)).Should().Be("bravo");
    }

    [Fact]
    public async Task Remove_on_missing_key_is_idempotent()
    {
        var entry = Client.CreateEntry<string>(Key("remove-missing"));
        await entry.Remove(Ct);
        (await entry.Exists(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Strongly_typed_complex_values_round_trip()
    {
        var entry = Client.CreateEntry<SamplePayload>(Key("complex")).WithAbsoluteTtl(TimeSpan.FromMinutes(5));
        await entry.Set(new SamplePayload(42, "Hitchhiker", ["alpha", "beta"]), Ct);
        var value = await entry.Get(Ct);
        value.Should().NotBeNull();
        value!.Id.Should().Be(42);
        value.Name.Should().Be("Hitchhiker");
        value.Tags.Should().Equal("alpha", "beta");
    }

    private ICacheClient Client => _client
        ?? throw new InvalidOperationException("Cache conformance host did not initialize.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static CacheKey Key(string purpose) => new($"conformance-{purpose}-{Guid.CreateVersion7():N}");

    public sealed record SamplePayload(int Id, string Name, string[] Tags);
}
