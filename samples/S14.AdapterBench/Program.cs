using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Core;
using Koan.Web.Extensions;
using Microsoft.Data.Sqlite;
using S14.AdapterBench.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure SQLite for optimal benchmark performance
// Use WAL mode and aggressive caching for better write performance
var sqliteConnectionString = "Data Source=Data/Koan.sqlite;Mode=ReadWriteCreate;Cache=Shared;Pooling=true";
builder.Configuration["Koan:Data:Sqlite:ConnectionString"] = sqliteConnectionString;

// Koan framework auto-registration
builder.Services.AddKoan();

// SignalR for real-time benchmark progress
builder.Services.AddSignalR();

// Benchmark service
builder.Services.AddSingleton<IBenchmarkService, BenchmarkService>();

var app = builder.Build();

// Apply SQLite performance optimizations at startup
await InitializeSqlitePerformanceAsync(app.Logger);

// Koan.Web startup filter auto-wires static files, controller routing, and Swagger

// Map SignalR hub
app.MapHub<S14.AdapterBench.Hubs.BenchmarkHub>("/hubs/benchmark");

app.Run();

static async Task InitializeSqlitePerformanceAsync(ILogger logger)
{
    try
    {
        logger.LogInformation("Initializing SQLite with performance optimizations");

        // Open a connection and configure performance settings
        using var connection = new SqliteConnection("Data Source=Data/Koan.sqlite");
        await connection.OpenAsync();

        // Apply performance PRAGMAs
        var pragmas = new[]
        {
            "PRAGMA journal_mode = WAL",           // Write-Ahead Logging for better concurrency
            "PRAGMA synchronous = NORMAL",         // Balance between safety and speed
            "PRAGMA cache_size = -64000",          // 64MB cache
            "PRAGMA temp_store = MEMORY",          // Use memory for temp tables
            "PRAGMA mmap_size = 268435456",        // 256MB memory-mapped I/O
            "PRAGMA page_size = 8192"              // Larger page size for efficiency
        };

        foreach (var pragma in pragmas)
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            var result = await command.ExecuteScalarAsync();
            logger.LogDebug("SQLite PRAGMA applied: {Pragma}, Result: {Result}", pragma, result);
        }

        logger.LogInformation("SQLite performance optimizations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to apply SQLite performance optimizations - benchmarks may run slower");
    }
}

namespace S14.AdapterBench
{
    public partial class Program { }
}
