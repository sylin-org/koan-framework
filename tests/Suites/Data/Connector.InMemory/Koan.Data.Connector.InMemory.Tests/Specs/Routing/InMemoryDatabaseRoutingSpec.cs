using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Routing;

/// <summary>
/// ARCH-0103 P2 (the `KeyValueStore` family base) — InMemory realizes <b>Database</b> mode on the data plane: a
/// Database-mode <c>[DataAxis]</c> auto-routes each operation to a distinct physical store derived from the ambient
/// (<see cref="InMemShardAmbient"/>), with <b>no explicit <c>EntityContext.Source</c></b> — proven through a real
/// <c>AddKoan()</c> boot with two in-memory sources. Docker-free.
///
/// <para>The mechanism is the data-plane twin of the P1 <c>InMemoryVector</c> proof: <c>InMemoryDataStore</c> keys each
/// physical store by <c>(routed source, entity type, partition)</c>, and <c>InMemoryAdapterFactory</c> threads the
/// routed source into the repository — so two shards land in two store dictionaries with no shared state. Physical
/// isolation is asserted by reading back under each shard (each sees only its own row) and cross-shard get-by-id
/// returning <c>null</c>; a fail-closed case proves the external-only posture.</para>
/// </summary>
public sealed class InMemoryDatabaseRoutingSpec : IAsyncLifetime
{
    [InMemSharded]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    private IntegrationHost? _host;

    public async ValueTask InitializeAsync()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",

            // Two in-memory sources — the routed source alone selects the physical store (no connection string).
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            ["Koan:Data:Sources:tenant_a:Adapter"] = "inmemory",
            ["Koan:Data:Sources:tenant_b:Adapter"] = "inmemory",
        };

        _host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);
        AppHost.Current = _host.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            if (ReferenceEquals(AppHost.Current, _host.Services)) AppHost.Current = null;
            await _host.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact(DisplayName = "Database-mode axis auto-routes InMemory writes to distinct physical stores by ambient shard")]
    public async Task Database_mode_axis_auto_routes_by_ambient_to_distinct_stores()
    {
        // No EntityContext.Source(...) anywhere — only the ambient shard. The Database-mode axis derives the source.
        Doc a, b;
        using (InMemShardAmbient.Use("tenant_a")) a = await new Doc { Title = "from-a" }.Save();
        using (InMemShardAmbient.Use("tenant_b")) b = await new Doc { Title = "from-b" }.Save();

        using (InMemShardAmbient.Use("tenant_a"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-a");
            (await Doc.Get(b.Id)).Should().BeNull();   // tenant_b's row is unreachable from tenant_a
        }

        using (InMemShardAmbient.Use("tenant_b"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-b");
            (await Doc.Get(a.Id)).Should().BeNull();   // and vice-versa
        }
    }

    [Fact(DisplayName = "Database-mode route to an unconfigured InMemory source fails closed (external-only, self-explaining)")]
    public async Task Routing_to_an_unconfigured_source_fails_closed()
    {
        // ARCH-0102 §3 FC-7: the realized posture is external-only — routing to an unconfigured source throws a
        // self-explaining error rather than silently mis-routing. tenant_z is never configured by this fixture.
        Func<Task> act = async () =>
        {
            using (InMemShardAmbient.Use("tenant_z"))
                await new Doc { Title = "nope" }.Save();
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*tenant_z*not configured (provisioning posture: ExternalOnly)*");
    }
}

// ==================== Assembly-local shard fixtures (the [Sharded]/ambient axis, mirrors the Axes suite) ====================

/// <summary>A discoverable Database-mode axis: an <see cref="InMemShardedAttribute"/> entity routes each op to the data
/// source named by the ambient shard. Inert when no shard is in scope (the provider returns null ⇒ fall-through).</summary>
public sealed class InMemShardRouteAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("inmem-shard-route")
        .Mode(AxisMode.Database)
        .AppliesTo(InMemShardMetadata.IsSharded)
        .Field("shard", static () => InMemShardAmbient.Current, typeof(string));   // the per-operation SOURCE-KEY provider
}

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class InMemShardedAttribute : Attribute;

internal static class InMemShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<InMemShardedAttribute>(inherit: true) is not null);
}

/// <summary>The ambient shard scope — selects the in-memory store an <see cref="InMemShardedAttribute"/> entity routes to.</summary>
public static class InMemShardAmbient
{
    private static readonly AsyncLocal<string?> _shard = new();
    public static string? Current => _shard.Value;
    public static IDisposable Use(string? shard)
    {
        var prev = _shard.Value;
        _shard.Value = shard;
        return new Scope(prev);
    }
    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (_done) return; _done = true; _shard.Value = previous; }
    }
}
