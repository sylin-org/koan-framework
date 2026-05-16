using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Extensions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Cache.CrossEngine.Specs;

/// <summary>
/// Abstraction-proof scenarios: every behavior in this class is part of the
/// <see cref="ICacheClient"/> contract — adapter-independent. Subclasses bind a specific
/// engine via <see cref="ConfigureAdapter"/>; xUnit runs the inherited <c>[Fact]</c> methods
/// against each subclass's engine. If both subclasses pass the same scenarios with identical
/// observable behavior, the abstraction holds.
/// </summary>
/// <remarks>
/// <para>
/// What this proves: the cache pillar's surface (<c>ICacheClient.CreateEntry&lt;T&gt;(key)</c>,
/// terminal verbs <c>Get</c>/<c>Set</c>/<c>Remove</c>/<c>Exists</c>/<c>Touch</c>, TTL
/// semantics, key isolation) is honored identically by the Memory and Sqlite stores.
/// Adapter-specific extras (persistence across host restart, on-disk file shape) belong in
/// adapter-specific specs — those tests would fail meaningfully on the wrong engine, so
/// they're NOT part of this canon.
/// </para>
/// <para>
/// What this does NOT prove: distributed coherence (cross-node invalidation), L2 fallback
/// semantics, or anything requiring a real Redis. Those need their own multi-host harness.
/// </para>
/// </remarks>
public abstract class CrossEngineCacheBehaviorSpecBase : IAsyncDisposable
{
    private IntegrationHost? _host;

    /// <summary>
    /// Subclass wires its engine here. Called inside <c>KoanIntegrationHost.Configure().ConfigureServices(...)</c>;
    /// the universal cache infrastructure (<c>AddKoanCache</c>, serializers, topology) is added
    /// before this runs.
    /// </summary>
    protected abstract void ConfigureAdapter(IServiceCollection services);

    /// <summary>
    /// Human-readable engine name surfaced in assertion messages. Helps disambiguate
    /// "which subclass failed" in test output.
    /// </summary>
    protected abstract string EngineName { get; }

    /// <summary>
    /// Build the host once per test method (xUnit constructs the subclass once per <c>[Fact]</c>).
    /// </summary>
    private async ValueTask<ICacheClient> BuildClient(CancellationToken ct)
    {
        _host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddKoanCache();
                ConfigureAdapter(services);
            })
            .StartAsync(ct);
        return _host.Services.GetRequiredService<ICacheClient>();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Set_then_Get_returns_the_value()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"set-get-{Guid.NewGuid():N}");

        await client.CreateEntry<string>(key).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set("hello", ct);
        var value = await client.CreateEntry<string>(key).Get(ct);

        value.Should().Be("hello", $"{EngineName}: Set then Get must return the written value");
    }

    [Fact]
    public async Task Get_on_missing_key_returns_default()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"missing-{Guid.NewGuid():N}");

        var value = await client.CreateEntry<string>(key).Get(ct);

        value.Should().BeNull($"{EngineName}: Get on a never-set key must return null (the abstraction's miss signal)");
    }

    [Fact]
    public async Task Exists_returns_true_after_Set_and_false_after_Remove()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"exists-{Guid.NewGuid():N}");

        (await client.CreateEntry<string>(key).Exists(ct))
            .Should().BeFalse($"{EngineName}: a never-set key must not Exist");

        await client.CreateEntry<string>(key).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set("v", ct);
        (await client.CreateEntry<string>(key).Exists(ct))
            .Should().BeTrue($"{EngineName}: after Set, Exists must return true");

        await client.CreateEntry<string>(key).Remove(ct);
        (await client.CreateEntry<string>(key).Exists(ct))
            .Should().BeFalse($"{EngineName}: after Remove, Exists must return false");
    }

    [Fact]
    public async Task Set_overwrites_the_previous_value()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"overwrite-{Guid.NewGuid():N}");

        await client.CreateEntry<string>(key).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set("first", ct);
        await client.CreateEntry<string>(key).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set("second", ct);
        var value = await client.CreateEntry<string>(key).Get(ct);

        value.Should().Be("second", $"{EngineName}: a second Set against the same key must overwrite");
    }

    [Fact]
    public async Task Different_keys_are_independent()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var keyA = new CacheKey($"isolation-a-{Guid.NewGuid():N}");
        var keyB = new CacheKey($"isolation-b-{Guid.NewGuid():N}");

        await client.CreateEntry<string>(keyA).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set("alpha", ct);
        await client.CreateEntry<string>(keyB).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set("bravo", ct);

        (await client.CreateEntry<string>(keyA).Get(ct)).Should().Be("alpha", $"{EngineName}: key isolation broke (A returned not-alpha)");
        (await client.CreateEntry<string>(keyB).Get(ct)).Should().Be("bravo", $"{EngineName}: key isolation broke (B returned not-bravo)");

        await client.CreateEntry<string>(keyA).Remove(ct);
        (await client.CreateEntry<string>(keyB).Get(ct)).Should().Be("bravo", $"{EngineName}: removing A must not affect B");
    }

    [Fact]
    public async Task Remove_on_missing_key_is_a_no_op()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"remove-missing-{Guid.NewGuid():N}");

        // Must not throw — the abstraction treats Remove as idempotent.
        await client.CreateEntry<string>(key).Remove(ct);
        (await client.CreateEntry<string>(key).Exists(ct))
            .Should().BeFalse($"{EngineName}: Remove on a missing key must remain a no-op");
    }

    [Fact]
    public async Task Strongly_typed_round_trip_for_complex_value()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"complex-{Guid.NewGuid():N}");
        var payload = new SamplePayload(Id: 42, Name: "Hitchhiker", Tags: ["alpha", "beta"]);

        await client.CreateEntry<SamplePayload>(key).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set(payload, ct);
        var roundTrip = await client.CreateEntry<SamplePayload>(key).Get(ct);

        roundTrip.Should().NotBeNull($"{EngineName}: complex-value round-trip lost the entry");
        roundTrip!.Id.Should().Be(42);
        roundTrip.Name.Should().Be("Hitchhiker");
        roundTrip.Tags.Should().BeEquivalentTo(new[] { "alpha", "beta" });
    }

    public sealed record SamplePayload(int Id, string Name, string[] Tags);
}
