using System.Security.Cryptography;
using System.Text;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using DocumentFormat.OpenXml.Packaging;

namespace Koan.Samples.Meridian.Services;

public interface ITextExtractor
{
    Task<TextExtractionResult> ExtractAsync(SourceDocument document, CancellationToken ct);
}

public sealed class TextExtractionResult
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; } = 0.0;
    public int PageCount { get; init; } = 0;
    public string Method { get; init; } = "Unknown";
}

public sealed class TextExtractor : ITextExtractor
{
    private readonly IDocumentStorage _storage;
    private readonly ILogger<TextExtractor> _logger;

    public TextExtractor(IDocumentStorage storage, ILogger<TextExtractor> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<TextExtractionResult> ExtractAsync(SourceDocument document, CancellationToken ct)
    {
        await using var stream = await _storage.OpenReadAsync(document.StorageKey, ct);
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var mediaType = document.MediaType?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            mediaType = InferMediaType(document.OriginalFileName);
        }

        if (mediaType is "application/pdf" or "pdf")
        {
            return ExtractFromPdf(buffer);
        }

        if (mediaType is "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/msword")
        {
            return ExtractFromDocx(buffer);
        }

        if (mediaType is "text/plain" or "text/markdown")
        {
            return await ExtractPlainTextAsync(buffer, ct);
        }

        _logger.LogWarning("Unknown media type {MediaType} for document {DocumentId}; falling back to plain text read.", mediaType, document.Id);
        return await ExtractPlainTextAsync(buffer, ct);
    }

    private static TextExtractionResult ExtractFromPdf(Stream pdfStream)
    {
        pdfStream.Position = 0;
        using var document = PdfDocument.Open(pdfStream);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            builder.AppendLine(text);
            builder.AppendLine();
        }

        return new TextExtractionResult
        {
            Text = builder.ToString(),
            Confidence = 0.95,
            PageCount = document.NumberOfPages,
            Method = "PdfPig"
        };
    }

    private async Task<TextExtractionResult> ExtractPlainTextAsync(Stream stream, CancellationToken ct)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        return new TextExtractionResult
        {
            Text = text,
            Confidence = 0.5,
            PageCount = Math.Max(1, CountLogicalPages(text)),
            Method = "PlainText"
        };
    }

    private TextExtractionResult ExtractFromDocx(Stream stream)
    {
        stream.Position = 0;
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null)
        {
            return new TextExtractionResult
            {
                Text = string.Empty,
                Confidence = 0.1,
                PageCount = 0,
                Method = "DocxEmpty"
            };
        }

        var builder = new StringBuilder();
        foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            builder.AppendLine(paragraph.InnerText);
        }

        var text = builder.ToString();
        return new TextExtractionResult
        {
            Text = text,
            Confidence = 0.9,
            PageCount = Math.Max(1, CountLogicalPages(text)),
            Method = "Docx"
        };
    }

    public static string ComputeTextHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string InferMediaType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            _ => "application/octet-stream"
        };
    }

    private static int CountLogicalPages(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // 1800 characters approximates a page of business prose.
        return Math.Max(1, (int)Math.Ceiling(text.Length / 1800.0));
    }
}
