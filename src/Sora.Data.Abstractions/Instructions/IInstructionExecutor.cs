namespace Sora.Data.Abstractions.Instructions;

public interface IInstructionExecutor<TEntity> where TEntity : class
{
    Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default);
}