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
using Koan.Redis;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Koan.Data.Connector.Redis.Tests.Specs.Routing;

/// <summary>
/// ARCH-0103 P2 (the `KeyValueStore` JSON-text family) — Redis realizes <b>Database</b> mode: a Database-mode
/// <c>[DataAxis]</c> auto-routes each operation to a distinct physical Redis store (a per-source <b>logical database</b>)
/// derived from the ambient (<see cref="RedisShardAmbient"/>), with <b>no explicit <c>EntityContext.Source</c></b> —
/// proven through a real <c>AddKoan()</c> boot over a single Redis container with two sources pinned to different logical
/// database indexes.
///
/// <para>The per-source database index is resolved by <c>RedisAdapterFactory</c> via the shared
/// <c>AdapterConnectionResolver.GetSourceSetting(..., "Database")</c> (the same primitive the relational trio uses for
/// its per-source connection string), and the repository selects it with <c>GetDatabase(index)</c> on the shared
/// connection — so each shard's keys live in their own physical keyspace. Physical isolation is asserted by reading back
/// under each shard (each sees only its own row) and cross-shard get-by-id returning <c>null</c>; a fail-closed case
/// proves the external-only posture.</para>
/// </summary>
public sealed class RedisDatabaseRoutingSpec : IAsyncLifetime
{
    [RedisSharded]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    private RedisContainer? _container;
    private IntegrationHost? _host;
    private string? _skip;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder("redis:7-alpine").Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _skip = $"Redis container unavailable: {ex.Message}";
            return;
        }

        var conn = _container.GetConnectionString();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["ConnectionStrings:Redis"] = conn,   // the shared backend connection (default logical db 0)

            // Three sources on the SAME Redis server, isolated by logical database index.
            ["Koan:Data:Sources:Default:Adapter"] = "redis",
            ["Koan:Data:Sources:Default:redis:Database"] = "0",
            ["Koan:Data:Sources:tenant_a:Adapter"] = "redis",
            ["Koan:Data:Sources:tenant_a:redis:Database"] = "1",
            ["Koan:Data:Sources:tenant_b:Adapter"] = "redis",
            ["Koan:Data:Sources:tenant_b:redis:Database"] = "2",
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
        if (_container is not null) await _container.DisposeAsync().ConfigureAwait(false);
    }

    [Fact(DisplayName = "Database-mode axis auto-routes Redis writes to distinct logical databases by ambient shard")]
    public async Task Database_mode_axis_auto_routes_by_ambient_to_distinct_stores()
    {
        Assert.SkipWhen(_host is null, _skip ?? "Redis unavailable");

        // No EntityContext.Source(...) anywhere — only the ambient shard. The Database-mode axis derives the source.
        Doc a, b;
        using (RedisShardAmbient.Use("tenant_a")) a = await new Doc { Title = "from-a" }.Save();
        using (RedisShardAmbient.Use("tenant_b")) b = await new Doc { Title = "from-b" }.Save();

        // Each shard reads back ONLY its own row — physical isolation (distinct logical databases).
        using (RedisShardAmbient.Use("tenant_a"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-a");
            (await Doc.Get(b.Id)).Should().BeNull();   // tenant_b's row is unreachable from tenant_a
        }

        using (RedisShardAmbient.Use("tenant_b"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-b");
            (await Doc.Get(a.Id)).Should().BeNull();   // and vice-versa
        }
    }

    [Fact(DisplayName = "Data Redis and the standard DI multiplexer use the shared backend connection")]
    public void Default_route_uses_the_shared_backend_connection()
    {
        Assert.SkipWhen(_host is null, _skip ?? "Redis unavailable");

        var provider = _host.Services.GetRequiredService<IRedisConnectionProvider>();
        var standard = _host.Services.GetRequiredService<IConnectionMultiplexer>();
        provider.GetDefault().Should().BeSameAs(standard);
    }

    [Fact(DisplayName = "Database-mode route to an unconfigured Redis source fails closed (external-only, self-explaining)")]
    public async Task Routing_to_an_unconfigured_source_fails_closed()
    {
        Assert.SkipWhen(_host is null, _skip ?? "Redis unavailable");

        // ARCH-0102 §3 FC-7: the realized posture is external-only — routing to an unconfigured source throws a
        // self-explaining error rather than silently mis-routing. tenant_z is never configured by this fixture.
        Func<Task> act = async () =>
        {
            using (RedisShardAmbient.Use("tenant_z"))
                await new Doc { Title = "nope" }.Save();
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*tenant_z*not configured (provisioning posture: ExternalOnly)*");
    }
}

// ==================== Assembly-local shard fixtures (the [Sharded]/ambient axis, mirrors the Axes suite) ====================

/// <summary>A discoverable Database-mode axis: a <see cref="RedisShardedAttribute"/> entity routes each op to the data
/// source named by the ambient shard. Inert when no shard is in scope (the provider returns null ⇒ fall-through).</summary>
public sealed class RedisShardRouteAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("redis-shard-route")
        .Mode(AxisMode.Database)
        .AppliesTo(RedisShardMetadata.IsSharded)
        .Field("shard", static () => RedisShardAmbient.Current, typeof(string));   // the per-operation SOURCE-KEY provider
}

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class RedisShardedAttribute : Attribute;

internal static class RedisShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<RedisShardedAttribute>(inherit: true) is not null);
}

/// <summary>The ambient shard scope — selects the Redis logical database a <see cref="RedisShardedAttribute"/> entity routes to.</summary>
public static class RedisShardAmbient
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
