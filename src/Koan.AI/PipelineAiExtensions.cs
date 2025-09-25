using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Models;
using Koan.Core.Pipelines;

namespace Koan.AI;

/// <summary>
/// Pipeline helpers that wrap Koan AI abstractions.
/// </summary>
public static class PipelineAiExtensions
{
    public static PipelineBuilder<TEntity> Tokenize<TEntity>(this IAsyncEnumerable<TEntity> source, Func<TEntity, string> textSelector, AiTokenizeOptions? options = null)
        => source.Pipeline().Tokenize(textSelector, options);

    public static PipelineBuilder<TEntity> Tokenize<TEntity>(this PipelineBuilder<TEntity> builder, Func<TEntity, string> textSelector, AiTokenizeOptions? options = null)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (textSelector is null) throw new ArgumentNullException(nameof(textSelector));
        options ??= AiTokenizeOptions.Default;

        return builder.AddStage(async (envelope, ct) =>
        {
            if (envelope.IsFaulted) return;

            try
            {
                var text = textSelector(envelope.Entity);
                if (string.IsNullOrWhiteSpace(text))
                {
                    envelope.RecordError(new InvalidOperationException("Tokenize stage received empty input."));
                    return;
                }

                var request = new AiEmbeddingsRequest { Model = options.Model };
                request.Input.Add(text);

                var response = await Ai.Embed(request, ct).ConfigureAwait(false);
                var vector = response.Vectors.FirstOrDefault();
                if (vector is null)
                {
                    envelope.RecordError(new InvalidOperationException("Embedding provider returned no vectors."));
                    return;
                }

                envelope.Features[PipelineFeatureKeys.Embedding] = vector;
                if (!string.IsNullOrEmpty(response.Model) || !string.IsNullOrEmpty(options.Model))
                {
                    envelope.Metadata[PipelineFeatureKeys.EmbeddingModel] = response.Model ?? options.Model!;
                }

                if (options.CaptureResponse)
                {
                    envelope.Metadata["embedding:raw"] = response;
                }
            }
            catch (Exception ex)
            {
                envelope.RecordError(ex);
            }
        });
    }
}

/// <summary>
/// Options for the AI tokenization pipeline stage.
/// </summary>
public sealed class AiTokenizeOptions
{
    public static AiTokenizeOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the model name passed to the embedding provider.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the raw embedding response should be captured in metadata.
    /// </summary>
    public bool CaptureResponse { get; init; }
}
