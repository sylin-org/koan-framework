using System.Runtime.CompilerServices;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Sqlite.Options;
using Koan.Data.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Adapter.Sqlite.Stores;

/// <summary>
/// Persistent local <see cref="ICacheStore"/> backed by SQLite. L1 candidate that survives
/// process restart. Higher <c>[ProviderPriority]</c> than the in-process Memory store so
/// referencing this adapter automatically takes precedence as the Local tier.
/// </summary>
[ProviderPriority(50)]
public sealed class SqliteCacheStore : ICacheStore, IDisposable
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS cache_entries (
            key TEXT PRIMARY KEY,
            value BLOB NOT NULL,
            content_kind INTEGER NOT NULL,
            runtime_type TEXT,
            created_utc TEXT NOT NULL,
            absolute_expiration_utc TEXT,
            stale_until_utc TEXT,
            tags TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_cache_tags ON cache_entries(tags);
        CREATE INDEX IF NOT EXISTS idx_cache_expiration ON cache_entries(absolute_expiration_utc);
        """;

    private readonly SqliteCacheOptions _options;
    private readonly ILogger<SqliteCacheStore> _logger;
    private readonly string _connectionString;
    private bool _initialized;
    private readonly object _initLock = new();

    public SqliteCacheStore(IOptions<SqliteCacheOptions> options, ILogger<SqliteCacheStore> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = BuildConnectionString(_options.DatabasePath);
    }

    public string Name => "sqlite";

    public CacheStorePlacement Placement => CacheStorePlacement.Local;

    public CacheStoreCapabilities Capabilities { get; } = new(
        SupportsTags: true,
        SupportsSlidingTtl: false,
        SupportsStaleWhileRevalidate: true,
        SupportsBinary: true,
        SupportsPersistence: true);

    public async ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheReadOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT value, content_kind, runtime_type, created_utc,
                   absolute_expiration_utc, stale_until_utc, tags
            FROM cache_entries WHERE key = @key
            """;
        command.Parameters.AddWithValue("@key", key.Value);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return CacheFetchResult.Miss(new CacheEntryOptions());

        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = ParseDateTimeOffset(reader, 4);
        var staleUntil = ParseDateTimeOffset(reader, 5);

        if (staleUntil is { } finalExpiry && finalExpiry <= now)
        {
            await RemoveCore(connection, key.Value, ct);
            return CacheFetchResult.Miss(new CacheEntryOptions());
        }

        if (absoluteExpiration is { } abs && abs <= now)
        {
            await RemoveCore(connection, key.Value, ct);
            return CacheFetchResult.Miss(new CacheEntryOptions());
        }

        var contentKind = (CacheContentKind)reader.GetInt32(1);
        var runtimeTypeName = reader.IsDBNull(2) ? null : reader.GetString(2);
        var runtimeType = runtimeTypeName is not null ? Type.GetType(runtimeTypeName) : null;
        var valueBlob = (byte[])reader[0];

        var cacheValue = contentKind switch
        {
            CacheContentKind.Binary => CacheValue.FromBytes(valueBlob, runtimeType),
            CacheContentKind.String => CacheValue.FromString(
                System.Text.Encoding.UTF8.GetString(valueBlob), runtimeType: runtimeType),
            CacheContentKind.Json => CacheValue.FromJson(
                System.Text.Encoding.UTF8.GetString(valueBlob), runtimeType),
            _ => CacheValue.FromBytes(valueBlob, runtimeType)
        };

        // Build a minimal CacheEntryOptions from the stored row's tags for the hit result.
        var tagsRaw = reader.IsDBNull(6) ? null : reader.GetString(6);
        var tagSet = string.IsNullOrEmpty(tagsRaw)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

        var hitOptions = new CacheEntryOptions { Tags = tagSet };
        return CacheFetchResult.HitResult(cacheValue, hitOptions, absoluteExpiration, staleUntil);
    }

    public async ValueTask Set(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = options.AbsoluteTtl.HasValue ? now.Add(options.AbsoluteTtl.Value) : (DateTimeOffset?)null;
        var staleUntil = absoluteExpiration;
        if (options.AllowStaleFor.HasValue && absoluteExpiration.HasValue)
            staleUntil = absoluteExpiration.Value.Add(options.AllowStaleFor.Value);

        var valueBytes = value.ToBytes().ToArray();
        var tagsString = options.Tags is { Count: > 0 } ? string.Join(",", options.Tags) : null;

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO cache_entries
                (key, value, content_kind, runtime_type, created_utc,
                 absolute_expiration_utc, stale_until_utc, tags)
            VALUES
                (@key, @value, @content_kind, @runtime_type, @created_utc,
                 @absolute_expiration_utc, @stale_until_utc, @tags)
            """;

        command.Parameters.AddWithValue("@key", key.Value);
        command.Parameters.AddWithValue("@value", valueBytes);
        command.Parameters.AddWithValue("@content_kind", (int)value.ContentKind);
        command.Parameters.AddWithValue("@runtime_type",
            (object?)value.RuntimeType?.AssemblyQualifiedName ?? DBNull.Value);
        command.Parameters.AddWithValue("@created_utc", FormatDateTimeOffset(now));
        command.Parameters.AddWithValue("@absolute_expiration_utc",
            absoluteExpiration.HasValue ? FormatDateTimeOffset(absoluteExpiration.Value) : DBNull.Value);
        command.Parameters.AddWithValue("@stale_until_utc",
            staleUntil.HasValue ? FormatDateTimeOffset(staleUntil.Value) : DBNull.Value);
        command.Parameters.AddWithValue("@tags", (object?)tagsString ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        return await RemoveCore(connection, key.Value, ct);
    }

    public async ValueTask Touch(CacheKey key, TimeSpan? newAbsoluteTtl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = newAbsoluteTtl.HasValue ? now.Add(newAbsoluteTtl.Value) : (DateTimeOffset?)null;

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE cache_entries
            SET absolute_expiration_utc = @absolute_expiration_utc,
                stale_until_utc = @stale_until_utc
            WHERE key = @key
            """;

        command.Parameters.AddWithValue("@key", key.Value);
        command.Parameters.AddWithValue("@absolute_expiration_utc",
            absoluteExpiration.HasValue ? FormatDateTimeOffset(absoluteExpiration.Value) : DBNull.Value);
        command.Parameters.AddWithValue("@stale_until_utc",
            absoluteExpiration.HasValue ? FormatDateTimeOffset(absoluteExpiration.Value) : DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<bool> Exists(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT absolute_expiration_utc, stale_until_utc
            FROM cache_entries WHERE key = @key
            """;
        command.Parameters.AddWithValue("@key", key.Value);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return false;

        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = ParseDateTimeOffset(reader, 0);
        var staleUntil = ParseDateTimeOffset(reader, 1);

        if (staleUntil is { } finalExpiry && finalExpiry <= now)
        {
            await RemoveCore(connection, key.Value, ct);
            return false;
        }

        if (absoluteExpiration is { } abs && abs <= now)
        {
            await RemoveCore(connection, key.Value, ct);
            return false;
        }

        return true;
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(
        string tag,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key, absolute_expiration_utc, stale_until_utc
            FROM cache_entries WHERE tags LIKE @tagPattern
            """;
        command.Parameters.AddWithValue("@tagPattern", $"%{EscapeLikePattern(tag)}%");

        await using var reader = await command.ExecuteReaderAsync(ct);
        var now = DateTimeOffset.UtcNow;

        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            var entryKey = reader.GetString(0);
            var absoluteExpiration = ParseDateTimeOffset(reader, 1);
            var staleUntil = ParseDateTimeOffset(reader, 2);

            if (staleUntil is { } finalExpiry && finalExpiry <= now) continue;
            if (absoluteExpiration is { } abs && abs <= now) continue;

            yield return new TaggedCacheKey(tag, new CacheKey(entryKey), absoluteExpiration);
        }
    }

    public void Dispose()
    {
        // SqliteConnection instances are transient; nothing to dispose at store level.
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            EnsureDirectoryExists(_options.DatabasePath);

            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = CreateTableSql;
            command.ExecuteNonQuery();

            using var walCommand = connection.CreateCommand();
            walCommand.CommandText = "PRAGMA journal_mode=WAL;";
            walCommand.ExecuteNonQuery();

            _logger.LogDebug("SQLite cache database initialized at {DatabasePath}", _options.DatabasePath);
            _initialized = true;
        }
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private static async Task<bool> RemoveCore(SqliteConnection connection, string key, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cache_entries WHERE key = @key";
        command.Parameters.AddWithValue("@key", key);

        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    private static string BuildConnectionString(string databasePath)
        => new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

    private static void EnsureDirectoryExists(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private static string FormatDateTimeOffset(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;

        var text = reader.GetString(ordinal);
        return DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;
    }

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
