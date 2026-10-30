using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.Direct;

public interface IDirectSession
{
    /// <summary>
    /// Uses <paramref name="value"/> as a literal physical connection-string override.
    /// </summary>
    /// <remarks>
    /// Empty values and <c>auto</c> are not physical connections. Use a source- or adapter-routed
    /// Direct session when Koan should resolve configuration or autonomous discovery.
    /// </remarks>
    IDirectSession WithConnectionString(string value);
    IDirectSession WithTimeout(TimeSpan timeout);
    IDirectSession WithMaxRows(int maxRows);
    /// <summary>
    /// Declares the effect of this opaque Direct operation or transaction once. The effect is the caller's declaration,
    /// and Koan does not check it against the SQL: a declared Read outside a transaction is cancelled when it stops at
    /// MaxRows. PostgreSQL enforces Read only on the entity-routed query, which it runs in a read-only transaction; on
    /// the untyped and typed reads, and on SQL Server, which has no read-only statement, a write labelled Read is
    /// cancelled at the bound, and the server rolls back the statement the cancel interrupts. Declare Write for a
    /// command that writes.
    /// </summary>
    IDirectSession Effect(DataOperationEffect effect);
    IDirectTransaction Begin(CancellationToken ct = default);

    Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default);
    Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default);
    /// <summary>The rows <paramref name="sql"/> returns, at most the session's MaxRows; <see cref="DirectRows{T}.Completion"/>
    /// says whether the command had more.</summary>
    Task<DirectRows<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default);
    /// <summary>The rows <paramref name="sql"/> returns as column-name dictionaries, at most the session's MaxRows;
    /// <see cref="DirectRows{T}.Completion"/> says whether the command had more.</summary>
    Task<DirectRows<object>> Query(string sql, object? parameters = null, CancellationToken ct = default);
}
