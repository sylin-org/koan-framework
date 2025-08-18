using System;
using System.Threading;
using System.Threading.Tasks;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core.Metadata;

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
    var mi = typeof(IDataService).GetMethod(nameof(IDataService.GetRepository));
    var gm = mi!.MakeGenericMethod(typeof(TEntity), keyType);
    var repo = gm.Invoke(data, null)!;
    if (repo is IInstructionExecutor<TEntity> exec)
        {
            return await exec.ExecuteAsync<TResult>(instruction, ct);
        }
        throw new NotSupportedException($"Repository for {typeof(TEntity).Name} does not support instruction '{instruction.Name}'.");
    }
}
