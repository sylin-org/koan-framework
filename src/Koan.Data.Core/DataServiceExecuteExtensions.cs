using Koan.Data.Abstractions.Instructions;

namespace Koan.Data.Core;

/// <summary>
/// Helper to execute typed instructions against a repository when the key type is not known at compile time.
/// </summary>
public static class DataServiceExecuteExtensions
{

    public static async Task<TResult> Execute<TEntity, TResult>(this IDataService data, string sql, object? parameters = null, CancellationToken ct = default) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL must be provided.", nameof(sql));
        var trimmed = sql.TrimStart();
        if (trimmed.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
        {
            // Delegate to query/scalar based on TResult; consumers can explicitly call InstructionSql.Scalar/Query.
            var instr = InstructionSql.Query(sql, parameters);
            return await data.Execute<TEntity, TResult>(instr, ct);
        }
        var nonQuery = InstructionSql.NonQuery(sql, parameters);
        if (typeof(TResult) == typeof(bool))
        {
            var affected = await data.Execute<TEntity, int>(nonQuery, ct);
            return (TResult)(object)(affected > 0);
        }
        return await data.Execute<TEntity, TResult>(nonQuery, ct);
    }
    /// <summary>
    /// Execute an instruction for the specified aggregate, resolving its key type from metadata.
    /// </summary>
    public static async Task<TResult> Execute<TEntity, TResult>(this IDataService data, Instruction instruction, CancellationToken ct = default) where TEntity : class
    {
        // Determine the key type via AggregateMetadata and call GetRepository<TEntity,TKey>() reflectively
        var id = AggregateMetadata.GetIdSpec(typeof(TEntity)) ?? throw new InvalidOperationException($"No Identifier on {typeof(TEntity).Name}");
        var keyType = id.Prop.PropertyType;
        // Prefer resolving the generic method on the concrete instance to avoid interface invocation quirks
        var targetType = data.GetType();
        var mi = targetType.GetMethod("GetRepository");
        if (mi is null)
        {
            mi = typeof(IDataService).GetMethod(nameof(IDataService.GetRepository));
        }
        if (mi is null) throw new InvalidOperationException("IDataService.GetRepository method not found.");
        var gm = mi.MakeGenericMethod(typeof(TEntity), keyType);
        var repo = gm.Invoke(data, null)!;
        if (repo is IInstructionExecutor<TEntity> exec)
        {
            return await exec.ExecuteAsync<TResult>(instruction, ct);
        }
        throw new NotSupportedException($"Repository for {typeof(TEntity).Name} does not support instruction '{instruction.Name}'.");
    }

    /// <summary>
    /// Execute an instruction when the key type is already known, avoiding reflection.
    /// </summary>
    public static async Task<TResult> Execute<TEntity, TKey, TResult>(this IDataService data, Instruction instruction, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        var repo = data.GetRepository<TEntity, TKey>();
        if (repo is IInstructionExecutor<TEntity> exec)
        {
            return await exec.ExecuteAsync<TResult>(instruction, ct);
        }
        throw new NotSupportedException($"Repository for {typeof(TEntity).Name} does not support instruction '{instruction.Name}'.");
    }
}
