using System.Data;
using System.Data.Common;

namespace Koan.Data.Relational.Ado;

/// <summary>
/// Raw-ADO.NET command helpers shared by AOT-targeted relational adapters (e.g. SQLite — the sovereign
/// single-binary floor). Deliberately Dapper-free: Dapper's <c>GetTypeDeserializerImpl</c> emits IL at runtime,
/// which NativeAOT forbids (<see cref="PlatformNotSupportedException"/>: "Dynamic code generation is not supported").
/// Koan entities persist as a single <c>(Id, Json)</c> row, so a hand-rolled reader is all that is needed — the
/// richer object mapping stays in Newtonsoft over the <c>Json</c> value. The Dapper-backed twin with the same
/// surface (for non-AOT adapters that benefit from Dapper) lives in <c>Koan.Data.Relational.Dapper</c>.
/// </summary>
/// <remarks>
/// Helpers accept <see cref="IDbConnection"/> (the SQLite adapter pools a wrapper) and obtain async ADO by casting
/// the created command to <see cref="DbCommand"/> — every real provider command derives from it. The connection is
/// expected to be open (the SQLite pool opens on rent); the command is disposed but the connection is not.
/// </remarks>
public static class AdoCommands
{
    /// <summary>Reads the canonical entity rows: <c>SELECT Id, Json …</c>.</summary>
    public static async Task<List<(string Id, string Json)>> QueryIdJsonAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        using var cmd = CreateCommand(conn, sql, parameters, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<(string Id, string Json)>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var json = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            rows.Add((id, json));
        }
        return rows;
    }

    /// <summary>Executes a non-query (INSERT/UPDATE/DELETE/DDL) and returns the affected row count.</summary>
    public static async Task<int> ExecuteAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        using var cmd = CreateCommand(conn, sql, parameters, tx);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Executes a scalar query; <see cref="DBNull"/> is normalised to <c>null</c>.</summary>
    public static async Task<object?> ExecuteScalarAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        using var cmd = CreateCommand(conn, sql, parameters, tx);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is DBNull ? null : result;
    }

    /// <summary>Executes a scalar query and coerces the result to <see cref="long"/> (0 when null).</summary>
    public static async Task<long> ExecuteScalarInt64Async(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        var value = await ExecuteScalarAsync(conn, sql, parameters, tx, ct).ConfigureAwait(false);
        return value is null ? 0L : Convert.ToInt64(value);
    }

    /// <summary>Reads arbitrary rows as case-insensitive column→value dictionaries (the raw-SQL escape hatch).</summary>
    public static async Task<List<IReadOnlyDictionary<string, object?>>> QueryRowsAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        using var cmd = CreateCommand(conn, sql, parameters, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var dict = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                dict[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(dict);
        }
        return rows;
    }

    private static DbCommand CreateCommand(IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx)
    {
        var cmd = (DbCommand)conn.CreateCommand();
        if (tx is not null) cmd.Transaction = (DbTransaction)tx;
        cmd.CommandText = parameters is null ? sql : parameters.Bind(cmd, sql);
        return cmd;
    }
}
