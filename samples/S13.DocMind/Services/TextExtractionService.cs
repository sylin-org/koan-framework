using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfPig;
using PdfPig.Content;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed class TextExtractionService : ITextExtractionService
{
    private readonly ILogger<TextExtractionService> _logger;
    private readonly DocMindProcessingOptions _processingOptions;

    public TextExtractionService(IOptions<DocMindProcessingOptions> options, ILogger<TextExtractionService> logger)
    {
        _logger = logger;
        _processingOptions = options.Value;
    }

    public async Task<DocumentExtractionResult> ExtractAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var path = document.Storage.Path;
        if (!File.Exists(path)) throw new FileNotFoundException("Stored document missing", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        string text;
        int pageCount = 1;
        var containsImages = false;

        switch (extension)
        {
            case ".pdf":
                (text, pageCount) = ExtractPdf(path);
                break;
            case ".docx":
                text = ExtractDocx(path);
                break;
            case ".png":
            case ".jpg":
            case ".jpeg":
                containsImages = true;
                text = await DescribeImageAsync(path, cancellationToken);
                break;
            default:
                text = await File.ReadAllTextAsync(path, cancellationToken);
                break;
        }

        var wordCount = CountWords(text);
        var chunks = BuildChunks(text, _processingOptions.ChunkSizeTokens);
        return new DocumentExtractionResult(text, chunks, wordCount, pageCount, containsImages);
    }

    private static (string Text, int PageCount) ExtractPdf(string path)
    {
        var builder = new StringBuilder();
        using var pdf = PdfDocument.Open(path);
        foreach (Page page in pdf.GetPages())
        {
            builder.AppendLine(page.Text);
            builder.AppendLine();
        }
        return (builder.ToString(), pdf.NumberOfPages);
    }

    private static string ExtractDocx(string path)
    {
        var builder = new StringBuilder();
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is not null)
        {
            foreach (var text in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
            {
                builder.Append(text.Text);
                builder.Append(' ');
            }
        }
        return builder.ToString();
    }

    private async Task<string> DescribeImageAsync(string path, CancellationToken cancellationToken)
    {
        // Image OCR is optional in the reference stack. Provide a friendly placeholder so downstream logic can proceed.
        _logger.LogInformation("Image {Path} queued for OCR placeholder", path);
        return await Task.FromResult($"Image placeholder for {Path.GetFileName(path)}");
    }

    private static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static IReadOnlyList<ExtractedChunk> BuildChunks(string text, int chunkTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<ExtractedChunk>();
        }

        chunkTokens = Math.Max(200, chunkTokens);
        var approxChars = chunkTokens * 4; // Rough heuristic: 1 token ~= 4 chars
        var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<ExtractedChunk>();
        var builder = new StringBuilder();
        var index = 0;
        foreach (var paragraph in paragraphs)
        {
            if (builder.Length + paragraph.Length > approxChars && builder.Length > 0)
            {
                chunks.Add(CreateChunk(index++, builder.ToString()));
                builder.Clear();
            }
            builder.AppendLine(paragraph.Trim());
            builder.AppendLine();
        }

        if (builder.Length > 0)
        {
            chunks.Add(CreateChunk(index++, builder.ToString()));
        }

        if (chunks.Count == 0)
        {
            chunks.Add(CreateChunk(0, text));
        }

        return chunks;
    }

    private static ExtractedChunk CreateChunk(int index, string content)
    {
        var summary = content.Length <= 160 ? content.Trim() : content[..160].Trim() + "…";
        return new ExtractedChunk(index, DocumentChannels.Text, content.Trim(), summary);
    }
}
