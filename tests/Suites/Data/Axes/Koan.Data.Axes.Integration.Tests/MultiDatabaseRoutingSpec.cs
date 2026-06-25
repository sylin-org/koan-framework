using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// ARCH-0102 §3 (Phase 2) — a <b>Database-mode</b> <c>[DataAxis]</c> AUTO-ROUTES each operation to a distinct physical
/// data source derived from the ambient (the <see cref="ShardAmbient"/>), with <b>no explicit
/// <c>EntityContext.Source</c> call</b> — proven through a real <c>AddKoan()</c> boot over TWO SQLite databases.
///
/// <para>The footgun this gate catches: declaring Database mode without the <c>AdapterResolver</c> routing hook yields
/// <i>no isolation and no error</i> — both shards land in the Default store. Physical isolation is asserted by reading
/// back under each shard (each sees only its own row) and cross-shard get-by-id returning <c>null</c>. SQLite file DBs
/// keep this gate Docker-free, so it runs everywhere.</para>
/// </summary>
public sealed class MultiDatabaseRoutingSpec : IAsyncLifetime
{
    [Sharded]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"koan-multidb-{Guid.CreateVersion7():n}");
    private IntegrationHost? _host;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sqlite:DdlPolicy"] = "AutoCreate",
            ["Koan:Data:Sqlite:ConnectionString"] = Conn("default"),

            // Three distinct SQLite databases — the Default plus one per shard.
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = Conn("default"),
            ["Koan:Data:Sources:tenant_a:Adapter"] = "sqlite",
            ["Koan:Data:Sources:tenant_a:ConnectionString"] = Conn("tenant_a"),
            ["Koan:Data:Sources:tenant_b:Adapter"] = "sqlite",
            ["Koan:Data:Sources:tenant_b:ConnectionString"] = Conn("tenant_b"),
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
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private string Conn(string name) => $"Data Source={Path.Combine(_root, name + ".db")}";

    [Fact(DisplayName = "Database-mode axis auto-routes writes to distinct physical SQLite sources by ambient shard")]
    public async Task Database_mode_axis_auto_routes_by_ambient_to_distinct_stores()
    {
        // No EntityContext.Source(...) anywhere — only the ambient shard. The Database-mode axis derives the source.
        Doc a, b;
        using (ShardAmbient.Use("tenant_a")) a = await new Doc { Title = "from-a" }.Save();
        using (ShardAmbient.Use("tenant_b")) b = await new Doc { Title = "from-b" }.Save();

        // Each shard reads back ONLY its own row — physical isolation (distinct SQLite files).
        using (ShardAmbient.Use("tenant_a"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-a");
            (await Doc.Get(b.Id)).Should().BeNull();   // tenant_b's row is unreachable from tenant_a
        }

        using (ShardAmbient.Use("tenant_b"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-b");
            (await Doc.Get(a.Id)).Should().BeNull();   // and vice-versa
        }
    }

    [Fact(DisplayName = "Database-mode route to an unconfigured source fails closed (external-only posture, self-explaining)")]
    public async Task Routing_to_an_unconfigured_source_fails_closed()
    {
        // ARCH-0102 §3 FC-7: the realized Phase-2 posture is external-only — routing to a source that is not configured
        // throws a self-explaining error (names the entity, the source, the posture, and the fix) rather than silently
        // mis-routing to the default store. tenant_z is never configured by this fixture.
        Func<Task> act = async () =>
        {
            using (ShardAmbient.Use("tenant_z"))
                await new Doc { Title = "nope" }.Save();
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*tenant_z*not configured (provisioning posture: ExternalOnly)*");
    }
}
