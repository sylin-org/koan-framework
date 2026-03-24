using S18.Prism.Models;

namespace S18.Prism.Services.Extraction;

public class TextExtractor : IContentExtractor
{
    public string[] SupportedMimeTypes =>
        ["text/plain", "text/markdown", "text/csv", "application/json"];

    public int Priority => 100;

    public async Task<IReadOnlyList<ContentBlock>> Extract(
        Stream content, string mimeType, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);

        var kind = mimeType switch
        {
            "text/csv" => ContentKind.Table,
            "application/json" => ContentKind.Data,
            _ => ContentKind.Text
        };

        return
        [
            new ContentBlock
            {
                Kind = kind,
                Content = text,
                Order = 0,
                Source = new ContentSource("stream", mimeType, Extractor: nameof(TextExtractor))
            }
        ];
    }
}
