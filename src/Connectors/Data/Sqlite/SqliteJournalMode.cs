namespace Koan.Data.Connector.Sqlite;

/// <summary>
/// The SQLite journal mode the connector sets on every writable file database it opens (<c>PRAGMA journal_mode</c>).
/// The mode is stored in the database file, so it outlives the connection that set it. An in-memory database and a
/// read-only connection keep the mode they have.
/// </summary>
public enum SqliteJournalMode
{
    /// <summary>Write-ahead log: readers do not block the writer and the writer does not block readers. The default.</summary>
    Wal,
    /// <summary>SQLite's own default: a rollback journal deleted at the end of each transaction.</summary>
    Delete,
    /// <summary>A rollback journal truncated to zero length instead of deleted.</summary>
    Truncate,
    /// <summary>A rollback journal whose header is zeroed instead of the file being deleted.</summary>
    Persist,
    /// <summary>A rollback journal held in memory; a crash mid-transaction can corrupt the database.</summary>
    Memory,
    /// <summary>No journal; a rollback is impossible and a crash mid-transaction can corrupt the database.</summary>
    Off
}
