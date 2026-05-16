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

    public async Task<IReadOnlyList<ContentBlock>> Extract(
        Stream content, string mimeType, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "AI fallback extractor invoked for MIME type {MimeType}", mimeType);

        // Detect binary by sampling first 8KB before reading the entire stream
        try
        {
            var sampleSize = (int)Math.Min(8192, content.Length);
            var buffer = new byte[sampleSize];
            var bytesRead = await content.ReadAsync(buffer.AsMemory(0, sampleSize), ct);
            content.Position = 0; // Reset for actual reading

            // Check for null bytes in sample — indicates binary content
            if (buffer.AsSpan(0, bytesRead).Contains((byte)0))
            {
                return
                [
                    new ContentBlock
                    {
                        Kind = ContentKind.Data,
                        Content = $"Binary file ({content.Length} bytes)",
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

            using var reader = new StreamReader(content, leaveOpen: true);
            var text = await reader.ReadToEndAsync(ct);

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
