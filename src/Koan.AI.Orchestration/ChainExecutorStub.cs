using System.Runtime.CompilerServices;

namespace Koan.AI.Orchestration;

/// <summary>
/// Placeholder implementation of <see cref="IChainExecutor"/>.
/// All operations throw <see cref="NotImplementedException"/> until an AI provider
/// (e.g., Koan.AI.Ollama, Koan.AI.OpenAI) is registered that can serve chat completions.
/// </summary>
internal sealed class ChainExecutorStub : IChainExecutor
{
    private const string Message =
        "Chain execution requires an AI provider package. " +
        "Add Koan.AI.Ollama, Koan.AI.OpenAI, or another chat-capable provider.";

    public Task<ChainResult> ExecuteAsync(ChainDefinition definition, object? variables, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public async IAsyncEnumerable<ChainChunk> StreamAsync(
        ChainDefinition definition,
        object? variables,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _ = ct;
        throw new NotImplementedException(Message);
#pragma warning disable CS0162 // Unreachable code — required for IAsyncEnumerable signature
        yield break;
#pragma warning restore CS0162
    }
}
