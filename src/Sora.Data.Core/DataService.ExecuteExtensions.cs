using Sora.Data.Abstractions.Instructions;

namespace Sora.Data.Core;

/// <summary>
/// Helper to execute typed instructions against a repository when the key type is not known at compile time.
/// </summary>
public static class DataServiceExecuteExtensions
{
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
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
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
