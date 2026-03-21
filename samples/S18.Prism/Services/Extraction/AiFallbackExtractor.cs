using S18.Prism.Models;

namespace S18.Prism.Services.Extraction;

public class AiFallbackExtractor : IContentExtractor
{
    private readonly ILogger<AiFallbackExtractor> _logger;

    public AiFallbackExtractor(ILogger<AiFallbackExtractor> logger)
    {
        _logger = logger;
    }

    public string[] SupportedMimeTypes => ["*/*"];

    public int Priority => 0; // Lowest priority — only used when no specific extractor matches

    public async Task<IReadOnlyList<ContentBlock>> ExtractAsync(
        Stream content, string mimeType, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "AI fallback extractor invoked for MIME type {MimeType}", mimeType);

        // Attempt to read as text; if the content is binary, store a placeholder
        try
        {
            using var reader = new StreamReader(content, leaveOpen: true);
            var text = await reader.ReadToEndAsync(ct);

            // Basic heuristic: if the text contains many null bytes, it's likely binary
            if (text.Length > 0 && text.Count(c => c == '\0') > text.Length / 10)
            {
                return
                [
                    new ContentBlock
                    {
                        Kind = ContentKind.Data,
                        Content = $"[Binary content: {mimeType}, awaiting AI extraction]",
                        Order = 0,
                        Source = new ContentSource("stream", mimeType, Extractor: nameof(AiFallbackExtractor)),
                        Meta = new Dictionary<string, string>
                        {
                            ["status"] = "pending-ai-extraction",
                            ["original-mime"] = mimeType
                        }
                    }
                ];
            }

            return
            [
                new ContentBlock
                {
                    Kind = ContentKind.Text,
                    Content = text,
                    Order = 0,
                    Source = new ContentSource("stream", mimeType, Extractor: nameof(AiFallbackExtractor))
                }
            ];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read content as text for MIME type {MimeType}", mimeType);

            return
            [
                new ContentBlock
                {
                    Kind = ContentKind.Data,
                    Content = $"[Unreadable content: {mimeType}]",
                    Order = 0,
                    Source = new ContentSource("stream", mimeType, Extractor: nameof(AiFallbackExtractor)),
                    Meta = new Dictionary<string, string>
                    {
                        ["status"] = "extraction-failed",
                        ["error"] = ex.Message
                    }
                }
            ];
        }
    }
}
