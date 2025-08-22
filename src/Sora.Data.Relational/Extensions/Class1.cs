using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core;

namespace Sora.Data.Relational.Extensions;

public static class DataServiceExecuteExtensions
{
    public static async Task<TResult> Execute<TEntity, TResult>(this IDataService data, string sql,
        object? parameters = null, CancellationToken ct = default) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL must be provided.", nameof(sql));
        var trimmed = sql.TrimStart();
        // Heuristic: SELECT routes to scalar/query; everything else is treated as non-query.
        if (trimmed.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
        {
            // Let adapters decide if it's scalar or rowset; consumers should prefer InstructionSql.Scalar/Query directly.
            var instr = InstructionSql.Query(sql, parameters);
            return await data.Execute<TEntity, TResult>(instr, ct);
        }

        // Non-query path: support TResult of int (affected) or bool (affected > 0)
        var nonQuery = InstructionSql.NonQuery(sql, parameters);
        if (typeof(TResult) == typeof(bool))
        {
            var affected = await data.Execute<TEntity, int>(nonQuery, ct);
            return (TResult)(object)(affected > 0);
        }
        return await data.Execute<TEntity, TResult>(nonQuery, ct);
    }
}