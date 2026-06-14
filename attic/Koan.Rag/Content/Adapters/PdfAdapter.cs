using Koan.AI.Contracts.Models;
using Koan.Rag.Abstractions;
using Koan.Rag.Content.Strategies;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Content.Adapters;

/// <summary>
/// Content adapter for PDF files. Handles mixed content PDFs:
/// text layers are extracted directly, image/scanned pages run through
/// the visual multi-round protocol.
/// <para>
/// Full PDF parsing (text extraction, page-by-page) requires a PDF library.
/// This adapter uses OCR as the primary extraction path — a production
/// implementation should add a proper PDF parser (iText, PdfPig, etc.)
/// as a dependency and prefer text layer extraction when available.
/// </para>
/// </summary>
[ContentAdapter(".pdf")]
internal sealed class PdfAdapter : ContentAdapterBase
{
    private readonly ILogger<PdfAdapter> _logger;

    public PdfAdapter(StrategyGenerator strategyGenerator, ILogger<PdfAdapter> logger)
        : base(strategyGenerator, logger)
    {
        _logger = logger;
    }

    public override string Id => "pdf";

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>([".pdf"]);

    public override IReadOnlySet<Modality> SupportedModalities { get; } =
        new HashSet<Modality>([Modality.Document]);

    public override async Task<ContentExtractionResult> Extract(
        ContentExtractionRequest request, CancellationToken ct = default)
    {
        // Strategy: OCR the PDF to extract text (handles both text and scanned PDFs)
        // A production implementation should:
        // 1. Try text layer extraction first (PdfPig, iText)
        // 2. For pages with images/diagrams, run visual multi-round protocol
        // 3. Merge text + visual interpretations

        _logger.LogDebug("Processing PDF: {Title}", request.DocumentTitle ?? "untitled");

        try
        {
            // Use OCR as primary extraction (works for both text and scanned PDFs)
            var ocrText = await Koan.AI.Client.Ocr(request.Bytes, ct);

            if (string.IsNullOrWhiteSpace(ocrText))
            {
                // Fallback: treat as image and run visual interpretation
                return await InterpretVisual(
                    request.Bytes, request.DocumentTitle, request.Directive,
                    corpusName: null, ct);
            }

            return new ContentExtractionResult
            {
                Text = ocrText,
                Classification = new ContentClassification
                {
                    Category = "document/pdf",
                    Description = "PDF document with extracted text"
                },
                StrategyUsed = "ocr",
                RoundsExecuted = 1
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PDF extraction failed, attempting visual interpretation");

            // Fallback to visual interpretation
            return await InterpretVisual(
                request.Bytes, request.DocumentTitle, request.Directive,
                corpusName: null, ct);
        }
    }
}
