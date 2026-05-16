using System;
using System.Collections.Generic;
using System.IO;

namespace Koan.Tests.Cache.CrossEngine.Specs;

/// <summary>
/// Runs <see cref="CrossEngineCacheBehaviorSpecBase"/> against the SQLite-backed store.
/// </summary>
/// <remarks>
/// The SQLite adapter is referenced as a NuGet-style package reference in this project's
/// csproj — <c>services.AddKoan()</c> picks it up via reflective auto-registrar discovery.
/// Only configuration distinguishes this spec from <see cref="MemoryEngineSpec"/>: the
/// <c>LocalProvider</c> pin and the database path. No service-collection wiring, no manual
/// <c>Initialize(...)</c>. This is the Reference = Intent contract under test.
/// </remarks>
public sealed class SqliteEngineSpec : CrossEngineCacheBehaviorSpecBase, IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"koan-cache-crossengine-{Guid.NewGuid():N}.db");

    protected override string LocalProvider => "sqlite";

    protected override IEnumerable<(string Key, string Value)> ExtraSettings()
    {
        yield return ("Koan:Cache:Adapters:Sqlite:DatabasePath", _databasePath);
        // Keep the sweeper idle in tests — deterministic lifecycle.
        yield return ("Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds", "3600");
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
