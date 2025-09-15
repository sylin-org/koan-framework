using Microsoft.Extensions.DependencyInjection;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;

namespace Koan.AI;

// Preferred, semantic facade over Ai. Keeps terse defaults and allows targeted selection.
public static class Engine
{
    public static bool IsAvailable => Ai.IsAvailable;
    public static IAi? Try() => Ai.TryResolve();

    // Default engine
    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Ai.Embed(req, ct);

    public static Task<AiChatResponse> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Ai.Prompt(message, model, opts, ct);

    public static IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Ai.Stream(message, model, opts, ct);

    // Targeted selection (provider and/or model)
    public static EngineSelector For(string? provider = null, string? model = null) => new(provider, model);
}