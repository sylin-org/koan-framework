using System;
using System.Linq;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;

namespace S13.DocMind.Services;

public sealed class EmbeddingGenerator : IEmbeddingGenerator
{
    private readonly IAi? _ai;
    private readonly DocMindOptions _options;
    private readonly ILogger<EmbeddingGenerator> _logger;

    public EmbeddingGenerator(IServiceProvider serviceProvider, IOptions<DocMindOptions> options, ILogger<EmbeddingGenerator> logger)
    {
        _ai = serviceProvider.GetService<IAi>();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmbeddingGenerationResult> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        if (_ai is null || string.IsNullOrWhiteSpace(text))
        {
            return EmbeddingGenerationResult.Empty;
        }

        try
        {
            var started = DateTimeOffset.UtcNow;
            var response = await _ai.EmbedAsync(new AiEmbeddingsRequest
            {
                Model = _options.Ai.EmbeddingModel,
                Input = { text }
            }, cancellationToken).ConfigureAwait(false);

            var duration = DateTimeOffset.UtcNow - started;
            return new EmbeddingGenerationResult(response.Vectors.FirstOrDefault(), duration, response.Model);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding generation failed");
            return EmbeddingGenerationResult.Empty;
        }
    }
}
