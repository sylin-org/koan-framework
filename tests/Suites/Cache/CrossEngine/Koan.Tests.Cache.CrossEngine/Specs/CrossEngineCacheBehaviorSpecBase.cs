using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Cache.CrossEngine.Specs;

/// <summary>
/// Abstraction-proof scenarios: every behavior in this class is part of the
/// <see cref="ICacheClient"/> contract — adapter-independent. Subclasses select an engine
/// via <see cref="LocalProvider"/> (configuration only — no service-collection wiring);
/// xUnit runs the inherited <c>[Fact]</c> methods against each subclass's engine. If both
/// subclasses pass the same scenarios with identical observable behavior, the abstraction
/// holds AND <b>Reference = Intent</b> works: the only thing that changes between subclasses
/// is one config value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Koan-canonical wiring:</b> the spec calls <c>services.AddKoan()</c> (reflective
/// discovery) — never <c>AddKoanCache()</c> or <c>new SomeAutoRegistrar().Initialize(...)</c>.
/// The test project references both <c>Koan.Cache</c> (Memory default) AND
/// <c>Koan.Cache.Adapter.Sqlite</c>; both adapters' <c>KoanAutoRegistrar</c>s are picked up
/// by reflection. <c>Koan:Cache:LocalProvider</c> tells the topology resolver which one wins.
/// </para>
/// <para>
/// What this proves: the cache pillar's surface (<c>ICacheClient.CreateEntry&lt;T&gt;(key)</c>,
/// terminal verbs, TTL semantics, key isolation) is honored identically by the Memory and
/// SQLite stores AND the package-reference-driven discovery picks them up correctly.
/// </para>
/// <para>
/// What this does NOT prove: distributed coherence, L2 fallback semantics, Redis. Those
/// need their own multi-host harness.
/// </para>
/// </remarks>
public abstract class CrossEngineCacheBehaviorSpecBase : IAsyncDisposable
{
    private IntegrationHost? _host;

    /// <summary>
    /// Engine name as <c>ICacheStore.Name</c> exposes it. Used as the value of
    /// <c>Koan:Cache:LocalProvider</c> — the ONLY thing that differs between subclasses.
    /// </summary>
    protected abstract string LocalProvider { get; }

    /// <summary>
    /// Subclasses may publish additional configuration their engine needs (e.g., SQLite needs
    /// a database path). Returns an enumerable of (key, value) pairs added to the configuration
    /// builder via <c>WithSetting</c>. Default is empty — pure-default engines (Memory) need
    /// nothing.
    /// </summary>
    protected virtual IEnumerable<(string Key, string Value)> ExtraSettings()
        => Array.Empty<(string, string)>();

    private async ValueTask<ICacheClient> BuildClient(CancellationToken ct)
    {
        var builder = KoanIntegrationHost.Configure()
            .WithSetting("Koan:Cache:LocalProvider", LocalProvider);

        foreach (var (key, value) in ExtraSettings())
            builder = builder.WithSetting(key, value);

        _host = await builder
            // Reference = Intent: AddKoan() does reflective auto-registrar discovery across
            // every referenced Koan.* assembly. No AddKoanCache, no manual registrar init.
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(ct);

        // Sanity (public API only): the adapter's auto-registrar must have fired — i.e.,
        // Reference = Intent worked, the store is present in the registry. Without this, a
        // future refactor that breaks adapter discovery would silently degrade both subclasses
        // to whatever the default is, and the behavioral tests would still pass.
        var registry = _host.Services.GetRequiredService<ICacheStoreRegistry>();
        registry.FindByName(LocalProvider).Should().NotBeNull(
            $"adapter '{LocalProvider}' must be discovered via Reference = Intent. Either the package " +
            $"reference is missing from the test csproj or the adapter's KoanAutoRegistrar isn't firing.");

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

        value.Should().Be("hello", $"{LocalProvider}: Set then Get must return the written value");
    }

    [Fact]
    public async Task Get_on_missing_key_returns_default()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"missing-{Guid.NewGuid():N}");

        var value = await client.CreateEntry<string>(key).Get(ct);

        value.Should().BeNull($"{LocalProvider}: Get on a never-set key must return null (the abstraction's miss signal)");
    }

    [Fact]
    public async Task Exists_returns_true_after_Set_and_false_after_Remove()
    {
        var ct = CancellationToken.None;
        var client = await BuildClient(ct);
        var key = new CacheKey($"exists-{Guid.NewGuid():N}");

        (await client.CreateEntry<string>(key).Exists(ct))
            .Should().BeFalse($"{LocalProvider}: a never-set key must not Exist");

        await client.CreateEntry<string>(key).WithAbsoluteTtl(TimeSpan.FromMinutes(5)).Set("v", ct);
        (await client.CreateEntry<string>(key).Exists(ct))
            .Should().BeTrue($"{LocalProvider}: after Set, Exists must return true");

        await client.CreateEntry<string>(key).Remove(ct);
        (await client.CreateEntry<string>(key).Exists(ct))
            .Should().BeFalse($"{LocalProvider}: after Remove, Exists must return false");
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

        value.Should().Be("second", $"{LocalProvider}: a second Set against the same key must overwrite");
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

        (await client.CreateEntry<string>(keyA).Get(ct)).Should().Be("alpha", $"{LocalProvider}: key isolation broke (A returned not-alpha)");
        (await client.CreateEntry<string>(keyB).Get(ct)).Should().Be("bravo", $"{LocalProvider}: key isolation broke (B returned not-bravo)");

        await client.CreateEntry<string>(keyA).Remove(ct);
        (await client.CreateEntry<string>(keyB).Get(ct)).Should().Be("bravo", $"{LocalProvider}: removing A must not affect B");
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
            .Should().BeFalse($"{LocalProvider}: Remove on a missing key must remain a no-op");
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

        roundTrip.Should().NotBeNull($"{LocalProvider}: complex-value round-trip lost the entry");
        roundTrip!.Id.Should().Be(42);
        roundTrip.Name.Should().Be("Hitchhiker");
        roundTrip.Tags.Should().BeEquivalentTo(new[] { "alpha", "beta" });
    }

    public sealed record SamplePayload(int Id, string Name, string[] Tags);
}
