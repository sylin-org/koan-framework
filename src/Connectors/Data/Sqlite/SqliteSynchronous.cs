namespace Koan.Data.Connector.Sqlite;

/// <summary>
/// How often SQLite flushes to durable storage on every connection the connector opens (<c>PRAGMA synchronous</c>).
/// </summary>
public enum SqliteSynchronous
{
    /// <summary>Sync at every critical moment; a committed transaction survives power loss. SQLite's default, and Koan's.</summary>
    Full,
    /// <summary>
    /// Sync at every checkpoint under <see cref="SqliteJournalMode.Wal"/>: the database cannot corrupt, but the last
    /// transactions before a power loss can roll back. The usual choice for throughput.
    /// </summary>
    Normal,
    /// <summary>Like <see cref="Full"/>, and also syncs the directory after a rollback journal is deleted.</summary>
    Extra,
    /// <summary>Never sync; a power loss can corrupt the database.</summary>
    Off
}
