using Koan.AI.Contracts.Models;
using Koan.Rag.Abstractions;
using Koan.Rag.Content.Strategies;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Content.Adapters;

/// <summary>
/// Content adapter for image files. Runs the full multi-round visual
/// interpretation protocol: classify → select strategy → interpret → enrich.
/// </summary>
[ContentAdapter(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".svg")]
internal sealed class ImageAdapter : ContentAdapterBase
{
    public ImageAdapter(StrategyGenerator strategyGenerator, ILogger<ImageAdapter> logger)
        : base(strategyGenerator, logger) { }

    public override string Id => "image";

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>([".png", ".jpg", ".jpeg", ".gif", ".bmp",
            ".webp", ".tiff", ".tif", ".svg"]);

    public override IReadOnlySet<Modality> SupportedModalities { get; } =
        new HashSet<Modality>([Modality.Image]);

    public override Task<ContentExtractionResult> Extract(
        ContentExtractionRequest request, CancellationToken ct = default)
    {
        return InterpretVisual(
            request.Bytes,
            request.DocumentTitle,
            request.Directive,
            corpusName: null,
            ct);
    }
}
