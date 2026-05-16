using System;
using System.IO;
using Koan.Cache.Adapter.Sqlite.Initialization;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Cache.CrossEngine.Specs;

/// <summary>
/// Runs <see cref="CrossEngineCacheBehaviorSpecBase"/> against the SQLite-backed L1 store.
/// SQLite's adapter has higher priority than Memory, so the topology resolver picks it.
/// </summary>
/// <remarks>
/// Uses a unique temp database file per test method (xUnit constructs the subclass fresh per
/// <c>[Fact]</c>). Cleanup runs in <c>DisposeAsync</c> via <c>IAsyncDisposable</c> on the base
/// class, plus a best-effort file delete here.
/// </remarks>
public sealed class SqliteEngineSpec : CrossEngineCacheBehaviorSpecBase, IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"koan-cache-crossengine-{Guid.NewGuid():N}.db");

    protected override string EngineName => "Sqlite";

    protected override void ConfigureAdapter(IServiceCollection services)
    {
        // The Sqlite adapter reads its database path from configuration. We can't reach the
        // KoanIntegrationHost.Configure().WithSetting builder from here (it's already been
        // resolved by the time the base class calls us), so configure via DI directly. The
        // adapter's options binding will pick this up.
        services.Configure<Koan.Cache.Adapter.Sqlite.Options.SqliteCacheOptions>(o =>
        {
            o.DatabasePath = _databasePath;
            // Keep the sweeper idle in tests — deterministic lifecycle.
            o.SweepIntervalSeconds = 3600;
        });

        // Manual registrar invocation (mirrors SqliteCachePersistenceSpec's pattern).
        new KoanAutoRegistrar().Initialize(services);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch
        {
            // ignored — best-effort cleanup
        }
    }
}
