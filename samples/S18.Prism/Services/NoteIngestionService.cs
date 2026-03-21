using Koan.Data.Core;
using S18.Prism.Models;
using S18.Prism.Services.Extraction;

namespace S18.Prism.Services;

public class NoteIngestionService : INoteIngestionService
{
    private readonly IEnumerable<IContentExtractor> _extractors;
    private readonly ILogger<NoteIngestionService> _logger;

    public NoteIngestionService(
        IEnumerable<IContentExtractor> extractors,
        ILogger<NoteIngestionService> logger)
    {
        _extractors = extractors;
        _logger = logger;
    }

    public async Task<Note> IngestFileAsync(
        Stream content, string fileName, string? contentType, string spaceId, CancellationToken ct = default)
    {
        var mimeType = contentType ?? ResolveMimeType(fileName);
        var extractor = FindExtractor(mimeType);

        _logger.LogInformation(
            "Ingesting file {FileName} ({MimeType}) into space {SpaceId} using {Extractor}",
            fileName, mimeType, spaceId, extractor.GetType().Name);

        var blocks = await extractor.ExtractAsync(content, mimeType, ct);

        var note = new Note
        {
            Title = Path.GetFileNameWithoutExtension(fileName),
            SpaceId = spaceId,
            Origin = NoteOrigin.Upload,
            Blocks = blocks.ToList(),
            ExtractorUsed = extractor.GetType().Name
        };

        await note.Save(ct);

        _logger.LogInformation("Ingested note {NoteId} from file {FileName} ({BlockCount} blocks)",
            note.Id, fileName, blocks.Count);

        return note;
    }

    public async Task<Note> IngestUrlAsync(string url, string spaceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Ingesting URL {Url} into space {SpaceId}", url, spaceId);

        // URL ingestion creates a placeholder note; actual fetching is deferred to background workers
        var note = new Note
        {
            Title = url,
            SpaceId = spaceId,
            Origin = NoteOrigin.Source,
            SourceUrl = url,
            Blocks =
            [
                new ContentBlock
                {
                    Kind = ContentKind.Text,
                    Content = $"Pending content extraction from: {url}",
                    Order = 0,
                    Source = new ContentSource(url, Extractor: "UrlPending")
                }
            ]
        };

        await note.Save(ct);

        _logger.LogInformation("Created pending note {NoteId} for URL {Url}", note.Id, url);

        return note;
    }

    public async Task<Note> IngestTextAsync(string text, string? title, string spaceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Ingesting text into space {SpaceId}", spaceId);

        var note = new Note
        {
            Title = title ?? text[..Math.Min(80, text.Length)].Trim(),
            SpaceId = spaceId,
            Origin = NoteOrigin.Upload,
            Blocks =
            [
                new ContentBlock
                {
                    Kind = ContentKind.Text,
                    Content = text,
                    Order = 0,
                    Source = new ContentSource("inline-text", "text/plain", Extractor: "Direct")
                }
            ],
            ExtractorUsed = "Direct"
        };

        await note.Save(ct);

        _logger.LogInformation("Ingested text note {NoteId}", note.Id);

        return note;
    }

    private IContentExtractor FindExtractor(string mimeType)
    {
        var extractor = _extractors
            .Where(e => e.SupportedMimeTypes.Any(m =>
                m.Equals(mimeType, StringComparison.OrdinalIgnoreCase) || m == "*/*"))
            .OrderByDescending(e => e.Priority)
            .FirstOrDefault();

        if (extractor is null)
        {
            _logger.LogWarning("No extractor found for MIME type {MimeType}, using fallback", mimeType);
            return _extractors
                .FirstOrDefault(e => e.SupportedMimeTypes.Contains("*/*"))
                ?? throw new InvalidOperationException($"No content extractor available for MIME type: {mimeType}");
        }

        return extractor;
    }

    private static string ResolveMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".pdf" => "application/pdf",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }
}
