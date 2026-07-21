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
using Testcontainers.MongoDb;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Routing;

/// <summary>
/// ARCH-0103 P3 (the golden Mongo dialect) — Mongo realizes <b>Database</b> mode: a Database-mode <c>[DataAxis]</c>
/// auto-routes each operation to a distinct physical Mongo <b>database</b> derived from the ambient
/// (<see cref="MongoShardAmbient"/>), with <b>no explicit <c>EntityContext.Source</c></b> — proven through a real
/// <c>AddKoan()</c> boot over one Mongo container with two sources pinned to different databases.
///
/// <para>The per-source database is resolved by <c>MongoAdapterFactory</c> (which pools ONE provider per
/// connection+database via <c>AdapterConnectionResolver.GetSourceSetting(…,"Database")</c>), so each shard's documents
/// live in their own physical database. Physical isolation is asserted by reading back under each shard (each sees only
/// its own row) and cross-shard get-by-id returning <c>null</c>; a fail-closed case proves the external-only posture.</para>
/// </summary>
public sealed class MongoDatabaseRoutingSpec : IAsyncLifetime
{
    [MongoSharded]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    private MongoDbContainer? _container;
    private IntegrationHost? _host;
    private string? _skip;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MongoDbBuilder("mongo:8.3.4").Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _skip = $"Mongo container unavailable: {ex.Message}";
            return;
        }

        var conn = _container.GetConnectionString();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Mongo:ConnectionString"] = conn,
            ["Koan:Data:Mongo:Database"] = "koan",

            // Three sources on the SAME Mongo server, isolated by physical database.
            ["Koan:Data:Sources:Default:Adapter"] = "mongo",
            ["Koan:Data:Sources:Default:mongo:ConnectionString"] = conn,
            ["Koan:Data:Sources:Default:mongo:Database"] = "koan",
            ["Koan:Data:Sources:tenant_a:Adapter"] = "mongo",
            ["Koan:Data:Sources:tenant_a:mongo:ConnectionString"] = conn,
            ["Koan:Data:Sources:tenant_a:mongo:Database"] = "koan_a",
            ["Koan:Data:Sources:tenant_b:Adapter"] = "mongo",
            ["Koan:Data:Sources:tenant_b:mongo:ConnectionString"] = conn,
            ["Koan:Data:Sources:tenant_b:mongo:Database"] = "koan_b",
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

    [Fact(DisplayName = "Database-mode axis auto-routes Mongo writes to distinct physical databases by ambient shard")]
    public async Task Database_mode_axis_auto_routes_by_ambient_to_distinct_stores()
    {
        Assert.SkipWhen(_host is null, _skip ?? "Mongo unavailable");

        // No EntityContext.Source(...) anywhere — only the ambient shard. The Database-mode axis derives the source.
        Doc a, b;
        using (MongoShardAmbient.Use("tenant_a")) a = await new Doc { Title = "from-a" }.Save();
        using (MongoShardAmbient.Use("tenant_b")) b = await new Doc { Title = "from-b" }.Save();

        using (MongoShardAmbient.Use("tenant_a"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-a");
            (await Doc.Get(b.Id)).Should().BeNull();   // tenant_b's row is unreachable from tenant_a
        }

        using (MongoShardAmbient.Use("tenant_b"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-b");
            (await Doc.Get(a.Id)).Should().BeNull();   // and vice-versa
        }
    }

    [Fact(DisplayName = "Database-mode route to an unconfigured Mongo source fails closed (external-only, self-explaining)")]
    public async Task Routing_to_an_unconfigured_source_fails_closed()
    {
        Assert.SkipWhen(_host is null, _skip ?? "Mongo unavailable");

        // ARCH-0102 §3 FC-7: the realized posture is external-only — routing to an unconfigured source throws a
        // self-explaining error rather than silently mis-routing. tenant_z is never configured by this fixture.
        Func<Task> act = async () =>
        {
            using (MongoShardAmbient.Use("tenant_z"))
                await new Doc { Title = "nope" }.Save();
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*tenant_z*not configured (provisioning posture: ExternalOnly)*");
    }
}

// ==================== Assembly-local shard fixtures (the [Sharded]/ambient axis, mirrors the Axes suite) ====================

/// <summary>A discoverable Database-mode axis: a <see cref="MongoShardedAttribute"/> entity routes each op to the data
/// source named by the ambient shard. Inert when no shard is in scope (the provider returns null ⇒ fall-through).</summary>
public sealed class MongoShardRouteAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("mongo-shard-route")
        .Mode(AxisMode.Database)
        .AppliesTo(MongoShardMetadata.IsSharded)
        .Field("shard", static () => MongoShardAmbient.Current, typeof(string));   // the per-operation SOURCE-KEY provider
}

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class MongoShardedAttribute : Attribute;

internal static class MongoShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<MongoShardedAttribute>(inherit: true) is not null);
}

/// <summary>The ambient shard scope — selects the Mongo database a <see cref="MongoShardedAttribute"/> entity routes to.</summary>
public static class MongoShardAmbient
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
