using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Pipeline;

internal sealed class AdapterBackedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly AiCategoryRouter _routing;
    private readonly ILogger<AdapterBackedEmbeddingGenerator> _logger;

    public AdapterBackedEmbeddingGenerator(AiCategoryRouter routing, ILogger<AdapterBackedEmbeddingGenerator> logger)
    {
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));

        var request = ChatOptionsMapper.CreateEmbeddingRequest(values, options);
        var resolution = _routing.ResolveEmbeddings(request);

        var embedAdapter = resolution.Adapter as IEmbedAdapter
            ?? throw new InvalidOperationException(
                $"Adapter '{resolution.Adapter.Id}' does not implement IEmbedAdapter");

        request.InternalConnectionString = resolution.Member.ConnectionString;
        if (!string.IsNullOrWhiteSpace(resolution.EffectiveModel))
        {
            request = request with { Model = resolution.EffectiveModel };
        }

        var response = await embedAdapter.Embed(request, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Generated embeddings via adapter {Adapter} ({Category}) for model {Model}",
            resolution.Adapter.Id,
            resolution.Category,
            response.Model ?? resolution.EffectiveModel ?? "unknown");

        return ChatResponseMapper.FromAiEmbeddingsResponse(response);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to dispose.
    }
}
