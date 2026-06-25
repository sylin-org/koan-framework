using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

namespace Koan.Data.Connector.Json.Tests.Specs.Routing;

/// <summary>
/// ARCH-0103 P2 (the `KeyValueStore` JSON-text family) — Json realizes <b>Database</b> mode: a Database-mode
/// <c>[DataAxis]</c> auto-routes each operation to a distinct physical Json store (a per-source <b>directory</b>) derived
/// from the ambient (<see cref="JsonShardAmbient"/>), with <b>no explicit <c>EntityContext.Source</c></b> — proven
/// through a real <c>AddKoan()</c> boot over TWO Json source directories. Docker-free.
///
/// <para>The per-source directory is resolved by <c>JsonAdapterFactory</c> via the shared
/// <c>AdapterConnectionResolver.GetSourceSetting(..., "DirectoryPath")</c> (the same primitive the relational trio uses
/// for its per-source connection string), so each shard's writes land in its own directory tree. Physical isolation is
/// asserted by reading back under each shard (each sees only its own row) and cross-shard get-by-id returning
/// <c>null</c>; a fail-closed case proves the external-only posture (an unconfigured source throws, not mis-routes).</para>
/// </summary>
public sealed class JsonDatabaseRoutingSpec : IAsyncLifetime
{
    [JsonSharded]
    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"koan-json-multidb-{Guid.CreateVersion7():n}");
    private IntegrationHost? _host;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Json:DirectoryPath"] = Dir("default"),

            // Three distinct Json directories — the Default plus one per shard.
            ["Koan:Data:Sources:Default:Adapter"] = "json",
            ["Koan:Data:Sources:Default:json:DirectoryPath"] = Dir("default"),
            ["Koan:Data:Sources:tenant_a:Adapter"] = "json",
            ["Koan:Data:Sources:tenant_a:json:DirectoryPath"] = Dir("tenant_a"),
            ["Koan:Data:Sources:tenant_b:Adapter"] = "json",
            ["Koan:Data:Sources:tenant_b:json:DirectoryPath"] = Dir("tenant_b"),
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

    private string Dir(string name) => Path.Combine(_root, name);

    [Fact(DisplayName = "Database-mode axis auto-routes Json writes to distinct physical source directories by ambient shard")]
    public async Task Database_mode_axis_auto_routes_by_ambient_to_distinct_stores()
    {
        // No EntityContext.Source(...) anywhere — only the ambient shard. The Database-mode axis derives the source.
        Doc a, b;
        using (JsonShardAmbient.Use("tenant_a")) a = await new Doc { Title = "from-a" }.Save();
        using (JsonShardAmbient.Use("tenant_b")) b = await new Doc { Title = "from-b" }.Save();

        // Each shard reads back ONLY its own row — physical isolation (distinct directories).
        using (JsonShardAmbient.Use("tenant_a"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-a");
            (await Doc.Get(b.Id)).Should().BeNull();   // tenant_b's row is unreachable from tenant_a
        }

        using (JsonShardAmbient.Use("tenant_b"))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("from-b");
            (await Doc.Get(a.Id)).Should().BeNull();   // and vice-versa
        }

        // The isolation is physical: each shard's directory holds its own json file(s); the default holds none.
        Directory.EnumerateFiles(Dir("tenant_a"), "*.json", SearchOption.AllDirectories).Should().NotBeEmpty();
        Directory.EnumerateFiles(Dir("tenant_b"), "*.json", SearchOption.AllDirectories).Should().NotBeEmpty();
    }

    [Fact(DisplayName = "Database-mode route to an unconfigured Json source fails closed (external-only, self-explaining)")]
    public async Task Routing_to_an_unconfigured_source_fails_closed()
    {
        // ARCH-0102 §3 FC-7: the realized posture is external-only — routing to an unconfigured source throws a
        // self-explaining error (names the entity, the source, the posture, the fix) rather than silently mis-routing.
        Func<Task> act = async () =>
        {
            using (JsonShardAmbient.Use("tenant_z"))
                await new Doc { Title = "nope" }.Save();
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*tenant_z*not configured (provisioning posture: ExternalOnly)*");
    }
}

// ==================== Assembly-local shard fixtures (the [Sharded]/ambient/carrier triad, mirrors the Axes suite) ====================

/// <summary>A discoverable Database-mode axis: a <see cref="JsonShardedAttribute"/> entity routes each op to the data
/// source named by the ambient shard. Inert when no shard is in scope (the provider returns null ⇒ fall-through).</summary>
public sealed class JsonShardRouteAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("json-shard-route")
        .Mode(AxisMode.Database)
        .AppliesTo(JsonShardMetadata.IsSharded)
        .Field("shard", static () => JsonShardAmbient.Current, typeof(string))   // the per-operation SOURCE-KEY provider
        .Carries(new JsonShardCarrier());
}

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class JsonShardedAttribute : Attribute;

internal static class JsonShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<JsonShardedAttribute>(inherit: true) is not null);
}

/// <summary>The ambient shard scope — selects the Json source directory a <see cref="JsonShardedAttribute"/> entity routes to.</summary>
public static class JsonShardAmbient
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

/// <summary>Carries the ambient shard across the durable async hop (ARCH-0100) — Database-mode axes must Carries.</summary>
public sealed class JsonShardCarrier : IAmbientSliceCarrier
{
    public string AxisKey => "koan:json-shard-route";

    public string? Capture()
    {
        var shard = JsonShardAmbient.Current;
        return shard is null ? null : "v1:" + shard;
    }

    public IDisposable Restore(string captured)
    {
        if (captured.StartsWith("v1:", StringComparison.Ordinal))
            return JsonShardAmbient.Use(captured.Substring(3));
        throw new InvalidOperationException($"JsonShardCarrier cannot restore '{captured}' (unknown format).");
    }

    public IDisposable Suppress() => JsonShardAmbient.Use(null);
}
