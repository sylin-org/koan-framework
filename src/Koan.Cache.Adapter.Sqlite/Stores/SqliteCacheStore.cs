using System.Runtime.CompilerServices;
using System.Text.Json;
using Koan.Cache.Abstractions.Capabilities;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Sqlite.Options;
using Koan.Cache.Adapter.Sqlite.Infrastructure;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Adapter.Sqlite.Stores;

/// <summary>
/// Persistent local <see cref="ICacheStore"/> backed by SQLite. Referencing this adapter
/// elects it over the built-in memory provider for the Local tier.
/// </summary>
[ProviderPriority(Constants.ProviderPriority)]
public sealed class SqliteCacheStore : ICacheStore, IDisposable
{
    private const string CreateSchemaSql = """
        CREATE TABLE IF NOT EXISTS cache_entries (
            key TEXT PRIMARY KEY,
            value BLOB NOT NULL,
            content_kind INTEGER NOT NULL,
            runtime_type TEXT,
            created_utc TEXT NOT NULL,
            absolute_expiration_utc TEXT,
            stale_until_utc TEXT,
            tags TEXT,
            sliding_ttl_ms INTEGER
        );

        CREATE TABLE IF NOT EXISTS cache_entry_tags (
            entry_key TEXT NOT NULL,
            tag TEXT NOT NULL COLLATE NOCASE,
            PRIMARY KEY (entry_key, tag),
            FOREIGN KEY (entry_key) REFERENCES cache_entries(key) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_cache_expiration
            ON cache_entries(absolute_expiration_utc);
        CREATE INDEX IF NOT EXISTS idx_cache_entry_tags_tag
            ON cache_entry_tags(tag COLLATE NOCASE);
        """;

    private readonly SqliteCacheOptions _options;
    private readonly ILogger<SqliteCacheStore> _logger;
    private readonly string _connectionString;
    private readonly object _initLock = new();
    private bool _initialized;

    public SqliteCacheStore(IOptions<SqliteCacheOptions> options, ILogger<SqliteCacheStore> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = BuildConnectionString(_options.DatabasePath);
    }

    public string Name => Constants.ProviderId;
    public CacheStorePlacement Placement => CacheStorePlacement.Local;

    public void Describe(ICapabilities caps)
        => caps.Add(CacheCaps.Tags)
            .Add(CacheCaps.SlidingExpiration)
            .Add(CacheCaps.BoundedStaleServing)
            .Add(CacheCaps.BinaryPayload)
            .Add(CacheCaps.Persistent);

    public async ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheReadOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        byte[] valueBytes;
        CacheContentKind contentKind;
        Type? runtimeType;
        DateTimeOffset? absoluteExpiration;
        DateTimeOffset? staleUntil;
        TimeSpan? slidingTtl;
        IReadOnlySet<string> tags;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT value, content_kind, runtime_type, absolute_expiration_utc,
                       stale_until_utc, tags, sliding_ttl_ms
                FROM cache_entries WHERE key = @key
                """;
            command.Parameters.AddWithValue("@key", key.Value);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return CacheFetchResult.Miss(new CacheEntryOptions());

            valueBytes = (byte[])reader[0];
            contentKind = (CacheContentKind)reader.GetInt32(1);
            var runtimeTypeName = reader.IsDBNull(2) ? null : reader.GetString(2);
            runtimeType = runtimeTypeName is null ? null : Type.GetType(runtimeTypeName);
            absoluteExpiration = ParseDateTimeOffset(reader, 3);
            staleUntil = ParseDateTimeOffset(reader, 4);
            tags = ParseTags(reader.IsDBNull(5) ? null : reader.GetString(5));
            slidingTtl = reader.IsDBNull(6)
                ? null
                : TimeSpan.FromMilliseconds(reader.GetInt64(6));
        }

        var now = DateTimeOffset.UtcNow;
        if (staleUntil is { } finalExpiry && finalExpiry <= now)
        {
            await RemoveCore(connection, key.Value, ct);
            return CacheFetchResult.Miss(new CacheEntryOptions());
        }

        if (absoluteExpiration is { } expiredAt && expiredAt <= now && options.AllowStaleFor is null)
        {
            await RemoveCore(connection, key.Value, ct);
            return CacheFetchResult.Miss(new CacheEntryOptions());
        }

        if (slidingTtl is { } sliding && absoluteExpiration is { } previousExpiration && previousExpiration > now)
        {
            var staleWindow = staleUntil is { } previousStale && previousStale > previousExpiration
                ? previousStale - previousExpiration
                : (TimeSpan?)null;
            absoluteExpiration = now.Add(sliding);
            staleUntil = staleWindow is { } window
                ? absoluteExpiration.Value.Add(window)
                : absoluteExpiration;
            await UpdateExpiration(connection, key.Value, absoluteExpiration, staleUntil, ct);
        }

        var cacheValue = contentKind switch
        {
            CacheContentKind.Binary => CacheValue.FromBytes(valueBytes, runtimeType),
            CacheContentKind.String => CacheValue.FromString(System.Text.Encoding.UTF8.GetString(valueBytes), runtimeType: runtimeType),
            CacheContentKind.Json => CacheValue.FromJson(System.Text.Encoding.UTF8.GetString(valueBytes), runtimeType),
            _ => CacheValue.FromBytes(valueBytes, runtimeType)
        };

        var hitOptions = new CacheEntryOptions
        {
            Tags = tags,
            SlidingTtl = slidingTtl,
            AllowStaleFor = staleUntil is { } stale && absoluteExpiration is { } absolute && stale > absolute
                ? stale - absolute
                : null,
            ContentKind = contentKind
        };
        return CacheFetchResult.HitResult(cacheValue, hitOptions, absoluteExpiration, staleUntil);
    }

    public async ValueTask Set(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        var now = DateTimeOffset.UtcNow;
        var effectiveTtl = options.AbsoluteTtl ?? options.SlidingTtl;
        var absoluteExpiration = effectiveTtl is { } ttl ? now.Add(ttl) : (DateTimeOffset?)null;
        var staleUntil = absoluteExpiration is { } absolute && options.AllowStaleFor is { } stale
            ? absolute.Add(stale)
            : absoluteExpiration;

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cache_entries
                    (key, value, content_kind, runtime_type, created_utc,
                     absolute_expiration_utc, stale_until_utc, tags, sliding_ttl_ms)
                VALUES
                    (@key, @value, @content_kind, @runtime_type, @created_utc,
                     @absolute_expiration_utc, @stale_until_utc, @tags, @sliding_ttl_ms)
                ON CONFLICT(key) DO UPDATE SET
                    value = excluded.value,
                    content_kind = excluded.content_kind,
                    runtime_type = excluded.runtime_type,
                    created_utc = excluded.created_utc,
                    absolute_expiration_utc = excluded.absolute_expiration_utc,
                    stale_until_utc = excluded.stale_until_utc,
                    tags = excluded.tags,
                    sliding_ttl_ms = excluded.sliding_ttl_ms
                """;
            command.Parameters.AddWithValue("@key", key.Value);
            command.Parameters.AddWithValue("@value", value.ToBytes().ToArray());
            command.Parameters.AddWithValue("@content_kind", (int)value.ContentKind);
            command.Parameters.AddWithValue("@runtime_type", (object?)value.RuntimeType?.AssemblyQualifiedName ?? DBNull.Value);
            command.Parameters.AddWithValue("@created_utc", FormatDateTimeOffset(now));
            command.Parameters.AddWithValue("@absolute_expiration_utc", DbValue(absoluteExpiration));
            command.Parameters.AddWithValue("@stale_until_utc", DbValue(staleUntil));
            command.Parameters.AddWithValue("@tags", options.Tags.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(options.Tags));
            command.Parameters.AddWithValue("@sliding_ttl_ms", options.SlidingTtl is { } sliding ? (long)sliding.TotalMilliseconds : DBNull.Value);
            await command.ExecuteNonQueryAsync(ct);
        }

        await using (var clearTags = connection.CreateCommand())
        {
            clearTags.Transaction = transaction;
            clearTags.CommandText = "DELETE FROM cache_entry_tags WHERE entry_key = @key";
            clearTags.Parameters.AddWithValue("@key", key.Value);
            await clearTags.ExecuteNonQueryAsync(ct);
        }

        foreach (var tag in options.Tags.Where(static tag => !string.IsNullOrWhiteSpace(tag)))
        {
            await using var addTag = connection.CreateCommand();
            addTag.Transaction = transaction;
            addTag.CommandText = "INSERT OR IGNORE INTO cache_entry_tags(entry_key, tag) VALUES (@key, @tag)";
            addTag.Parameters.AddWithValue("@key", key.Value);
            addTag.Parameters.AddWithValue("@tag", tag.Trim());
            await addTag.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
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
        var expiration = newAbsoluteTtl is { } ttl ? DateTimeOffset.UtcNow.Add(ttl) : (DateTimeOffset?)null;
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await UpdateExpiration(connection, key.Value, expiration, expiration, ct);
    }

    public async ValueTask<bool> Exists(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        DateTimeOffset? staleUntil;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT stale_until_utc FROM cache_entries WHERE key = @key";
            command.Parameters.AddWithValue("@key", key.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false;
            staleUntil = ParseDateTimeOffset(reader, 0);
        }

        if (staleUntil is { } finalExpiry && finalExpiry <= DateTimeOffset.UtcNow)
        {
            await RemoveCore(connection, key.Value, ct);
            return false;
        }
        return true;
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.key, e.absolute_expiration_utc
            FROM cache_entries e
            INNER JOIN cache_entry_tags t ON t.entry_key = e.key
            WHERE t.tag = @tag COLLATE NOCASE
            """;
        command.Parameters.AddWithValue("@tag", tag);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            var absoluteExpiration = ParseDateTimeOffset(reader, 1);
            yield return new TaggedCacheKey(tag, new CacheKey(reader.GetString(0)), absoluteExpiration);
        }
    }

    public void Dispose()
    {
        using var connection = CreateConnection();
        SqliteConnection.ClearPool(connection);
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

            using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_keys=ON;";
                foreignKeys.ExecuteNonQuery();
            }
            EnsureSlidingColumn(connection);
            using (var schema = connection.CreateCommand())
            {
                schema.CommandText = CreateSchemaSql;
                schema.ExecuteNonQuery();
            }
            MigrateLegacyTags(connection);
            using (var wal = connection.CreateCommand())
            {
                wal.CommandText = "PRAGMA journal_mode=WAL;";
                wal.ExecuteNonQuery();
            }
            _logger.LogDebug("SQLite cache database initialized at {DatabasePath}", _options.DatabasePath);
            _initialized = true;
        }
    }

    private static void EnsureSlidingColumn(SqliteConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='cache_entries'";
        if (exists.ExecuteScalar() is null) return;

        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(cache_entries)";
        using var reader = columns.ExecuteReader();
        var hasColumn = false;
        while (reader.Read())
            hasColumn |= string.Equals(reader.GetString(1), "sliding_ttl_ms", StringComparison.OrdinalIgnoreCase);
        reader.Close();
        if (hasColumn) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE cache_entries ADD COLUMN sliding_ttl_ms INTEGER";
        alter.ExecuteNonQuery();
    }

    private static void MigrateLegacyTags(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT key, tags FROM cache_entries WHERE tags IS NOT NULL";
        using var reader = read.ExecuteReader();
        var rows = new List<(string Key, IReadOnlySet<string> Tags)>();
        while (reader.Read()) rows.Add((reader.GetString(0), ParseTags(reader.GetString(1))));
        reader.Close();

        foreach (var (key, tags) in rows)
        foreach (var tag in tags)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO cache_entry_tags(entry_key, tag) VALUES (@key, @tag)";
            insert.Parameters.AddWithValue("@key", key);
            insert.Parameters.AddWithValue("@tag", tag);
            insert.ExecuteNonQuery();
        }
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private static async Task<bool> RemoveCore(SqliteConnection connection, string key, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cache_entries WHERE key = @key";
        command.Parameters.AddWithValue("@key", key);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    private static async Task UpdateExpiration(SqliteConnection connection, string key, DateTimeOffset? absolute, DateTimeOffset? stale, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE cache_entries
            SET absolute_expiration_utc = @absolute, stale_until_utc = @stale
            WHERE key = @key
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@absolute", DbValue(absolute));
        command.Parameters.AddWithValue("@stale", DbValue(stale));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string BuildConnectionString(string databasePath)
        => new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString();

    private static void EnsureDirectoryExists(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static object DbValue(DateTimeOffset? value)
        => value is { } timestamp ? FormatDateTimeOffset(timestamp) : DBNull.Value;

    private static string FormatDateTimeOffset(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        return DateTimeOffset.TryParse(
            reader.GetString(ordinal),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var result)
            ? result
            : null;
    }

    private static IReadOnlySet<string> ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (value.TrimStart().StartsWith('['))
                return new HashSet<string>(JsonSerializer.Deserialize<string[]>(value) ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // Pre-1.0 rows used comma-delimited tags. Fall through to the migration parser.
        }
        return new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
    }
}
