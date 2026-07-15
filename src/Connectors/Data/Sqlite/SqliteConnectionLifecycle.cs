using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Logging;

namespace Koan.Data.Connector.Sqlite;

/// <summary>
/// Owns SQLite connection resources for one Koan host. Microsoft.Data.Sqlite remains responsible for ordinary
/// pooling; this lifecycle resolves each physical target once and records the pool groups observed by the host so
/// they can be cleared on shutdown. In-memory targets are host- and source-scoped and kept alive for the host
/// lifetime, which makes the same per-operation connection model useful for both files and memory databases.
/// </summary>
internal sealed class SqliteConnectionLifecycle(ILogger<SqliteConnectionLifecycle> logger) : IDisposable
{
    private readonly ReaderWriterLockSlim _lifetime = new(LockRecursionPolicy.NoRecursion);
    private readonly ConcurrentDictionary<TargetKey, Lazy<ConnectionTarget>> _targets =
        new(TargetKeyComparer.Instance);
    private bool _disposed;

    /// <summary>Create a closed connection owned by the caller and associated with this host lifecycle.</summary>
    public SqliteConnection Create(string configuredConnectionString, string source = "Default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredConnectionString);

        _lifetime.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var key = new TargetKey(NormalizeSource(source), configuredConnectionString);
            var target = _targets.GetOrAdd(
                key,
                static (candidate, owner) => new Lazy<ConnectionTarget>(
                    () => owner.Resolve(candidate),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                this).Value;

            return new SqliteConnection(target.ConnectionString);
        }
        finally
        {
            _lifetime.ExitReadLock();
        }
    }

    public void Dispose()
    {
        ConnectionTarget[] targets;

        _lifetime.EnterWriteLock();
        try
        {
            if (_disposed) return;
            _disposed = true;
            targets = _targets.Values
                .Where(static target => target.IsValueCreated)
                .Select(static target => target.Value)
                .ToArray();
            _targets.Clear();
        }
        finally
        {
            _lifetime.ExitWriteLock();
        }

        // A shared in-memory database disappears when its last connection closes. Dispose every keeper even when
        // one provider call fails so a single bad target cannot hold the remainder of the host open.
        foreach (var target in targets)
        {
            try
            {
                target.MemoryKeeper?.Dispose();
            }
            catch (Exception ex)
            {
                KoanLog.DataWarning(logger, "connection_lifecycle", "memory-close-failed",
                    ("connectionString", Redaction.DeIdentify(target.ConnectionString)),
                    ("error", Redaction.DeIdentify(ex.Message)));
            }
        }

        foreach (var connectionString in targets
                     .Select(static target => target.ConnectionString)
                     .Distinct(StringComparer.Ordinal))
        {
            try
            {
                using var representative = new SqliteConnection(connectionString);
                SqliteConnection.ClearPool(representative);
            }
            catch (Exception ex)
            {
                // Host shutdown remains best-effort, but an observed pool group that could not be released is visible.
                KoanLog.DataWarning(logger, "connection_lifecycle", "pool-clear-failed",
                    ("connectionString", Redaction.DeIdentify(connectionString)),
                    ("error", Redaction.DeIdentify(ex.Message)));
            }
        }
    }

    private ConnectionTarget Resolve(TargetKey key)
    {
        SqliteConnectionStringBuilder configured;
        try
        {
            configured = new SqliteConnectionStringBuilder(key.ConnectionString);
        }
        catch (ArgumentException ex)
        {
            // Preserve the actionable, secret-safe warning pinned by SqliteConnectionStringRedactionSpec. Opening
            // the returned connection remains the authoritative validation boundary.
            KoanLog.DataWarning(logger, "resolve_connection", "parse-failed",
                ("connectionString", Redaction.DeIdentify(key.ConnectionString)),
                ("error", Redaction.DeIdentify(ex.Message)));
            return new ConnectionTarget(key.ConnectionString, null);
        }

        if (IsMemory(configured))
        {
            var effective = new SqliteConnectionStringBuilder(configured.ConnectionString)
            {
                DataSource = $"koan-{Guid.CreateVersion7():n}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
            };
            var keeper = new SqliteConnection(effective.ConnectionString);
            keeper.Open();
            return new ConnectionTarget(effective.ConnectionString, keeper);
        }

        EnsureDirectory(configured, key.ConnectionString);
        return new ConnectionTarget(key.ConnectionString, null);
    }

    private void EnsureDirectory(SqliteConnectionStringBuilder configured, string connectionString)
    {
        var dataSource = configured.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)) return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(dataSource);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or System.Security.SecurityException)
        {
            KoanLog.DataDebug(logger, "resolve_path", "fullpath-failed",
                ("dataSource", Redaction.DeIdentify(dataSource)),
                ("error", Redaction.DeIdentify(ex.Message)));
            return;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Opening the returned connection surfaces the authoritative error if the directory is required.
            KoanLog.DataDebug(logger, "ensure_directory", "create-failed",
                ("directory", directory),
                ("connectionString", Redaction.DeIdentify(connectionString)),
                ("error", Redaction.DeIdentify(ex.Message)));
        }
    }

    private static bool IsMemory(SqliteConnectionStringBuilder builder)
        => builder.Mode == SqliteOpenMode.Memory
           || string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSource(string source)
        => string.IsNullOrWhiteSpace(source) ? "Default" : source.Trim();

    private readonly record struct TargetKey(string Source, string ConnectionString);

    private sealed class TargetKeyComparer : IEqualityComparer<TargetKey>
    {
        public static TargetKeyComparer Instance { get; } = new();

        public bool Equals(TargetKey x, TargetKey y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Source, y.Source)
               && StringComparer.Ordinal.Equals(x.ConnectionString, y.ConnectionString);

        public int GetHashCode(TargetKey value)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Source),
                StringComparer.Ordinal.GetHashCode(value.ConnectionString));
    }

    private sealed record ConnectionTarget(string ConnectionString, SqliteConnection? MemoryKeeper);
}
