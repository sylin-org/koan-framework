using System.Data;
using System.Data.Common;
using DuckDB.NET.Data;

namespace Koan.Data.Connector.DuckDb.Runtime;

/// <summary>
/// The transaction behind a read lane. The lane opens with <c>BEGIN TRANSACTION READ ONLY</c> —
/// DuckDB's engine-level guarantee that the lane cannot write — and this shim completes it with the
/// commit/rollback vocabulary the source integration speaks.
/// </summary>
internal sealed class ReadOnlyDuckDbTransaction(DuckDBConnection connection) : DbTransaction
{
    private bool _completed;

    protected override DbConnection? DbConnection => connection;
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

    public override void Commit()
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        Execute("COMMIT");
        _completed = true;
    }

    public override void Rollback()
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        Execute("ROLLBACK");
        _completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_completed)
        {
            try { Execute("ROLLBACK"); }
            catch { /* the connection is already gone; the engine rolls back with it */ }
            _completed = true;
        }
        base.Dispose(disposing);
    }

    private void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
