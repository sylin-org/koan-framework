using Koan.AI.Contracts.Models;
using Koan.Rag.Abstractions;
using Koan.Rag.Content.Strategies;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Content.Adapters;

/// <summary>
/// Content adapter for plain text documents (.txt, .md, .csv, .json, .xml, .html).
/// Simple text content is returned directly; structured text (code, tables)
/// runs through the multi-round protocol for richer interpretation.
/// </summary>
[ContentAdapter(".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".html", ".htm", ".yaml", ".yml", ".log")]
internal sealed class TextDocumentAdapter : ContentAdapterBase
{
    public TextDocumentAdapter(StrategyGenerator strategyGenerator, ILogger<TextDocumentAdapter> logger)
        : base(strategyGenerator, logger) { }

    public override string Id => "text-document";

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>([".txt", ".md", ".markdown", ".csv", ".json", ".xml",
            ".html", ".htm", ".yaml", ".yml", ".log"]);

    public override IReadOnlySet<Modality> SupportedModalities { get; } =
        new HashSet<Modality>([Modality.Text, Modality.Document]);

    public override async Task<ContentExtractionResult> Extract(
        ContentExtractionRequest request, CancellationToken ct = default)
    {
        var text = System.Text.Encoding.UTF8.GetString(request.Bytes);

        if (string.IsNullOrWhiteSpace(text))
            return ContentExtractionResult.Empty;

        // Plain text / markdown: return directly (no multi-round needed)
        var ext = request.FilePath is not null
            ? Path.GetExtension(request.FilePath).ToLowerInvariant()
            : ".txt";

        if (ext is ".txt" or ".md" or ".markdown" or ".log")
        {
            return new ContentExtractionResult
            {
                Text = text,
                Classification = new ContentClassification
                {
                    Category = "text/document",
                    Description = $"Plain text document ({ext})"
                },
                StrategyUsed = "passthrough",
                RoundsExecuted = 0
            };
        }

        // Structured text (CSV, JSON, XML, HTML): interpret for richer extraction
        return await InterpretText(
            text, request.DocumentTitle, request.Directive, corpusName: null, ct);
    }
}
