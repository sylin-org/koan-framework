using System;
using System.IO;
using System.Threading.Tasks;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 dockerless fixture for the SQLite relational adapter. No container — a unique temp-file
/// database is created once per assembly (<c>Data Source=...</c>) and deleted on dispose; specs isolate
/// via per-test partitions on the shared file (the same one-store-per-assembly + partition-isolation
/// model the container fixtures use). Mirrors the legacy <c>SqliteConnectorFixture</c> config.
/// </summary>
public sealed class SqliteFixture : KoanContainerFixture
{
    private string? _dbPath;

    public override string Engine => "sqlite";
    protected override string Adapter => "sqlite";

    /// <summary>The temp-file SQLite database path (created on start, deleted on dispose).</summary>
    public string DatabasePath => _dbPath ?? throw new InvalidOperationException("Fixture not initialized.");

    protected override Task<string> StartContainerAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"koan-sqlite-{Guid.CreateVersion7():n}.db");
        return Task.FromResult($"Data Source={_dbPath}");
    }

    protected override ValueTask StopContainerAsync()
    {
        if (_dbPath is not null)
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
            catch { /* best-effort temp cleanup */ }
        }
        return ValueTask.CompletedTask;
    }
}
