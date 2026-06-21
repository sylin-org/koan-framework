using System.Data;
using Dapper;
using Koan.Data.Relational.Ado;

namespace Koan.Data.Relational.Dapper;

/// <summary>
/// Dapper-backed twin of <see cref="AdoCommands"/> — the "little shim with Dapper" for non-AOT relational adapters
/// (e.g. Postgres, SQL Server, which are servers and never ship inside a single NativeAOT binary). It exposes the
/// same surface and the same <see cref="SqlParameters"/> model as the hand-rolled helpers, so an adapter that does
/// not target AOT can lean on Dapper's mapping/IN-expansion while an AOT adapter swaps to <see cref="AdoCommands"/>
/// without changing its call sites.
/// </summary>
/// <remarks>
/// Do NOT reference this from an adapter you intend to publish with NativeAOT: Dapper's deserializer/parameter
/// generators use <see cref="System.Reflection.Emit"/>, which throws at runtime under AOT. That is precisely the
/// split this shim formalises — Dapper here, raw ADO in <see cref="AdoCommands"/>.
/// </remarks>
public static class DapperCommands
{
    public static async Task<List<(string Id, string Json)>> QueryIdJsonAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<(string Id, string Json)>(new CommandDefinition(sql, ToDynamic(parameters), tx, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public static async Task<int> ExecuteAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
        => await conn.ExecuteAsync(new CommandDefinition(sql, ToDynamic(parameters), tx, cancellationToken: ct)).ConfigureAwait(false);

    public static async Task<object?> ExecuteScalarAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        var result = await conn.ExecuteScalarAsync(new CommandDefinition(sql, ToDynamic(parameters), tx, cancellationToken: ct)).ConfigureAwait(false);
        return result is DBNull ? null : result;
    }

    public static async Task<long> ExecuteScalarInt64Async(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        var value = await ExecuteScalarAsync(conn, sql, parameters, tx, ct).ConfigureAwait(false);
        return value is null ? 0L : Convert.ToInt64(value);
    }

    public static async Task<List<IReadOnlyDictionary<string, object?>>> QueryRowsAsync(
        IDbConnection conn, string sql, SqlParameters? parameters, IDbTransaction? tx, CancellationToken ct)
    {
        var rows = await conn.QueryAsync(new CommandDefinition(sql, ToDynamic(parameters), tx, cancellationToken: ct)).ConfigureAwait(false);
        var list = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, object?>((IDictionary<string, object?>)row, StringComparer.OrdinalIgnoreCase));
        }
        return list;
    }

    // Hand the parameters to Dapper as named values; an enumerable value triggers Dapper's own IN-expansion,
    // matching the hand-rolled SqlParameters.ExpandInClause behaviour.
    private static DynamicParameters? ToDynamic(SqlParameters? parameters)
    {
        if (parameters is null || parameters.Count == 0) return null;
        var dyn = new DynamicParameters();
        foreach (var (name, value) in parameters.Items) dyn.Add(name, value);
        return dyn;
    }
}
