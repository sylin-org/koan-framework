using System.Data.Common;

namespace Koan.Data.Core.Direct;

/// <summary>
/// How a bounded read leaves the rest of a result it will not read (DATA-0162). Npgsql and SqlClient drain the rest of a
/// result when its reader closes, so a read that stopped at its bound cancels the command first and the server stops
/// producing rows nobody reads — but only a read: a command that writes, or one inside a caller's transaction, is never
/// cancelled, because cancelling it would roll back or cut short work the caller asked for. Those drain to the end.
/// </summary>
public static class BoundedReads
{
    /// <summary>
    /// Whether a read that stops at its bound may cancel its command: its effect is a declared Read and no caller's
    /// transaction holds the connection. The effect is the caller's declaration: a write labelled Read is cancelled too,
    /// and the server rolls back the statement it interrupts (PostgreSQL refuses it earlier only in a read-only
    /// transaction; SQL Server cannot).
    /// </summary>
    public static bool MayCancel(Koan.Data.Abstractions.Sources.DataOperationEffect effect, bool inCallerTransaction) =>
        effect == Koan.Data.Abstractions.Sources.DataOperationEffect.Read && !inCallerTransaction;

    /// <summary>
    /// Closes <paramref name="reader"/> after its read stopped at the bound. With <paramref name="cancel"/>, the command is
    /// cancelled first, and a failure the abandoned rest raises while the reader closes — the cancellation itself, or a
    /// row past the bound that could not be produced — concerns rows nobody asked for and is not raised; the caller's own
    /// cancellation still is. Without it the reader drains to the end, and any failure there is raised.
    /// </summary>
    public static async ValueTask Close(DbCommand command, DbDataReader reader, bool cancel, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(reader);
        if (!cancel)
        {
            await reader.DisposeAsync().ConfigureAwait(false);
            return;
        }
        try
        {
            command.Cancel();
        }
        catch (Exception error) when (error is DbException or NotSupportedException or InvalidOperationException)
        {
            // A driver that cannot cancel drains the rest instead; the rows read are unaffected.
        }
        try
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
        catch (DbException) when (!ct.IsCancellationRequested)
        {
            // The cancellation, or a row past the bound, failed the abandoned rest of the result.
        }
    }
}
