using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Pipeline;

internal sealed class AdapterBackedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly AiRoutingEngine _routing;
    private readonly ILogger<AdapterBackedEmbeddingGenerator> _logger;

    public AdapterBackedEmbeddingGenerator(AiRoutingEngine routing, ILogger<AdapterBackedEmbeddingGenerator> logger)
    {
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));

        var request = ChatOptionsMapper.CreateEmbeddingRequest(values, options);
        var resolution = _routing.ResolveEmbeddings(request);

        request.InternalConnectionString = resolution.Member.ConnectionString;
        if (!string.IsNullOrWhiteSpace(resolution.EffectiveModel))
        {
            request = request with { Model = resolution.EffectiveModel };
        }

        var response = await resolution.Adapter.EmbedAsync(request, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Generated embeddings via adapter {Adapter} ({Capability}) for model {Model}",
            resolution.Adapter.Id,
            resolution.Capability,
            response.Model ?? resolution.EffectiveModel ?? "unknown");

        return ChatResponseMapper.FromAiEmbeddingsResponse(response);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to dispose.
    }
}
