using Koan.AI.Contracts.Models;
using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Content;

/// <summary>
/// Discovers and routes content to the appropriate adapter based on
/// file extension and modality. Auto-discovers adapters registered in DI.
/// </summary>
internal sealed class ContentAdapterRegistry
{
    private readonly IReadOnlyList<IContentAdapter> _adapters;
    private readonly ILogger<ContentAdapterRegistry> _logger;

    public ContentAdapterRegistry(
        IEnumerable<IContentAdapter> adapters,
        ILogger<ContentAdapterRegistry> logger)
    {
        _adapters = adapters.ToList();
        _logger = logger;

        _logger.LogDebug(
            "ContentAdapterRegistry initialized with {Count} adapters: {Adapters}",
            _adapters.Count,
            string.Join(", ", _adapters.Select(a => a.Id)));
    }

    /// <summary>
    /// Find the best adapter for the given request.
    /// Returns null if no adapter can handle the content.
    /// </summary>
    public IContentAdapter? Resolve(ContentExtractionRequest request)
    {
        // Try each adapter — first match wins
        foreach (var adapter in _adapters)
        {
            if (adapter.CanProcess(request))
                return adapter;
        }

        _logger.LogWarning(
            "No content adapter found for file '{File}' (modality: {Modality})",
            request.FilePath, request.Modality);

        return null;
    }

    /// <summary>
    /// Detect modality from file extension.
    /// </summary>
    public static Modality DetectModality(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".txt" or ".md" or ".markdown" or ".csv" or ".json" or
            ".xml" or ".html" or ".htm" or ".yaml" or ".yml" or ".log"
                => Modality.Text,

            ".pdf" or ".docx" or ".doc" or ".rtf" or ".odt"
                => Modality.Document,

            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or
            ".webp" or ".tiff" or ".tif" or ".svg"
                => Modality.Image,

            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or
            ".aac" or ".wma"
                => Modality.Audio,

            ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv"
                => Modality.Video,

            _ => Modality.Document // Default to document for unknown types
        };
    }

    /// <summary>
    /// Extract content from a file using the appropriate adapter.
    /// Falls back to raw text reading if no adapter matches.
    /// </summary>
    public async Task<ContentExtractionResult> ExtractFromFile(
        string filePath,
        string? directive,
        CancellationToken ct)
    {
        var fileInfo = new FileInfo(filePath);
        const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100 MB default
        if (fileInfo.Length > MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"File '{filePath}' exceeds maximum size ({fileInfo.Length / (1024 * 1024)} MB > {MaxFileSizeBytes / (1024 * 1024)} MB). " +
                "Configure Koan:Rag:MaxFileSizeBytes for larger files.");

        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        var modality = DetectModality(filePath);
        var documentTitle = Path.GetFileName(filePath);

        var request = new ContentExtractionRequest
        {
            FilePath = filePath,
            Bytes = bytes,
            Modality = modality,
            DocumentTitle = documentTitle,
            Directive = directive
        };

        var adapter = Resolve(request);

        if (adapter is not null)
        {
            _logger.LogDebug(
                "Using adapter '{Adapter}' for '{File}'", adapter.Id, filePath);
            return await adapter.Extract(request, ct);
        }

        // Fallback: try to read as UTF-8 text
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new ContentExtractionResult
                {
                    Text = text,
                    StrategyUsed = "fallback-utf8",
                    RoundsExecuted = 0
                };
            }
        }
        catch (Exception)
        {
            // Not valid UTF-8 — binary content without a matching adapter
        }

        _logger.LogWarning("Could not extract content from '{File}'", filePath);
        return ContentExtractionResult.Empty;
    }
}
