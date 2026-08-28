using System;
using System.IO;
using System.Threading.Tasks;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 dockerless fixture for the DuckDB embedded adapter. No container and no download — the
/// engine is linked into the test assembly via the native rider package. A unique temp-file database is
/// created once per assembly (<c>Data Source=...</c>) and deleted on dispose; specs isolate via per-test
/// partitions on the shared file, the same one-store-per-assembly model the other dockerless fixtures use.
/// </summary>
public sealed class DuckDbFixture : KoanContainerFixture
{
    private string? _dbPath;

    public override string Engine => "duckdb";
    protected override string Adapter => "duckdb";

    /// <summary>The temp-file DuckDB database path (created on start, deleted on dispose).</summary>
    public string DatabasePath => _dbPath ?? throw new InvalidOperationException("Fixture not initialized.");

    protected override Task<string> StartContainerAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"koan-duckdb-{Guid.CreateVersion7():n}.duckdb");
        return Task.FromResult($"Data Source={_dbPath}");
    }

    protected override ValueTask StopContainerAsync()
    {
        if (_dbPath is not null)
        {
            // DuckDB keeps its WAL next to the file; sweep both.
            foreach (var suffix in new[] { "", ".wal" })
            {
                var path = _dbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
        return ValueTask.CompletedTask;
    }
}
