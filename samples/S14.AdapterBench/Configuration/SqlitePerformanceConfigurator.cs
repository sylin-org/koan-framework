using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Koan.Data.Abstractions;

namespace S14.AdapterBench.Configuration;

/// <summary>
/// Configures SQLite connections with performance-optimized settings for benchmarking.
/// Applies WAL mode, aggressive caching, and write optimizations.
/// </summary>
public static class SqlitePerformanceConfigurator
{
    /// <summary>
    /// Configures a SQLite connection with performance-optimized PRAGMAs.
    /// </summary>
    public static async Task ConfigureForPerformanceAsync(SqliteConnection connection, ILogger? logger = null)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        logger?.LogInformation("Applying SQLite performance optimizations");

        // Use Write-Ahead Logging for better concurrency and performance
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode = WAL", logger);

        // Disable synchronous mode for maximum write speed (trade-off: less durability)
        // NORMAL = fsync only at critical moments (good balance)
        await ExecutePragmaAsync(connection, "PRAGMA synchronous = NORMAL", logger);

        // Increase cache size to 64MB (default is usually 2MB)
        // Negative value means KB, so -64000 = 64MB
        await ExecutePragmaAsync(connection, "PRAGMA cache_size = -64000", logger);

        // Use memory for temp storage
        await ExecutePragmaAsync(connection, "PRAGMA temp_store = MEMORY", logger);

        // Increase page size for better I/O efficiency (default is 4096)
        // Note: This only works on database creation
        await ExecutePragmaAsync(connection, "PRAGMA page_size = 8192", logger);

        // Enable memory-mapped I/O (256MB)
        await ExecutePragmaAsync(connection, "PRAGMA mmap_size = 268435456", logger);

        // Optimize for write-heavy workloads
        await ExecutePragmaAsync(connection, "PRAGMA locking_mode = EXCLUSIVE", logger);

        // Auto-vacuum for better space management
        await ExecutePragmaAsync(connection, "PRAGMA auto_vacuum = INCREMENTAL", logger);

        logger?.LogInformation("SQLite performance optimizations applied successfully");
    }

    private static async Task ExecutePragmaAsync(SqliteConnection connection, string pragma, ILogger? logger)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            var result = await command.ExecuteScalarAsync();
            logger?.LogDebug("SQLite PRAGMA executed: {Pragma}, Result: {Result}", pragma, result);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to execute SQLite PRAGMA: {Pragma}", pragma);
        }
    }

    /// <summary>
    /// Builds a performance-optimized SQLite connection string.
    /// </summary>
    public static string BuildOptimizedConnectionString(string filePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };

        return builder.ToString();
    }
}
