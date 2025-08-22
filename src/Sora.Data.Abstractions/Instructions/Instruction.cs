namespace Sora.Data.Abstractions.Instructions;

public sealed record Instruction(
    string Name,
    object? Payload = null,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    IReadOnlyDictionary<string, object?>? Options = null
);

public interface IInstructionExecutor<TEntity> where TEntity : class
{
    Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default);
}
