using System.Runtime.CompilerServices;

namespace Koan.AI.Agents;

/// <summary>
/// Placeholder implementation of <see cref="IAgentExecutor"/>.
/// All operations throw <see cref="NotImplementedException"/> until an AI provider
/// (e.g., Koan.AI.Ollama, Koan.AI.OpenAI) is registered that can serve chat completions.
/// </summary>
internal sealed class AgentExecutorStub : IAgentExecutor
{
    private const string Message =
        "Agent execution requires an AI provider package. " +
        "Add Koan.AI.Ollama, Koan.AI.OpenAI, or another chat-capable provider.";

    public Task<AgentResult> ExecuteAsync(AgentDefinition definition, string goal, object? context, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public async IAsyncEnumerable<AgentStep> StreamAsync(
        AgentDefinition definition,
        string goal,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _ = ct;
        throw new NotImplementedException(Message);
#pragma warning disable CS0162 // Unreachable code — required for IAsyncEnumerable signature
        yield break;
#pragma warning restore CS0162
    }
}
