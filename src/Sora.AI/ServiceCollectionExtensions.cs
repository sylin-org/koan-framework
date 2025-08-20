using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Options;

namespace Sora.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration? config = null)
    {
        services.AddOptions<AiOptions>();
        if (config is not null)
            services.Configure<AiOptions>(config.GetSection("Sora:Ai"));

        services.TryAddSingleton<IAi, DefaultAi>();
        return services;
    }
}

internal sealed class DefaultAi : IAi
{
    public Task<Sora.AI.Contracts.Models.AiChatResponse> PromptAsync(Sora.AI.Contracts.Models.AiChatRequest request, CancellationToken ct = default)
        => Task.FromException<Sora.AI.Contracts.Models.AiChatResponse>(new InvalidOperationException("No AI providers configured. Add an adapter (e.g., Ollama) or enable Dev auto-discovery."));

    public IAsyncEnumerable<Sora.AI.Contracts.Models.AiChatChunk> StreamAsync(Sora.AI.Contracts.Models.AiChatRequest request, CancellationToken ct = default)
        => ThrowStream();

    public Task<Sora.AI.Contracts.Models.AiEmbeddingsResponse> EmbedAsync(Sora.AI.Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct = default)
        => Task.FromException<Sora.AI.Contracts.Models.AiEmbeddingsResponse>(new InvalidOperationException("No AI providers configured. Add an adapter (e.g., Ollama) or enable Dev auto-discovery."));

    public Task<string> PromptAsync(string message, string? model = null, Sora.AI.Contracts.Models.AiPromptOptions? opts = null, CancellationToken ct = default)
        => Task.FromException<string>(new InvalidOperationException("No AI providers configured. Add an adapter (e.g., Ollama) or enable Dev auto-discovery."));

    public IAsyncEnumerable<Sora.AI.Contracts.Models.AiChatChunk> StreamAsync(string message, string? model = null, Sora.AI.Contracts.Models.AiPromptOptions? opts = null, CancellationToken ct = default)
        => ThrowStream();

    private static async IAsyncEnumerable<Sora.AI.Contracts.Models.AiChatChunk> ThrowStream()
    {
        throw new InvalidOperationException("No AI providers configured. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        yield break;
    }
}
