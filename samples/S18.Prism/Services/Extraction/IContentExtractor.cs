using S18.Prism.Models;

namespace S18.Prism.Services.Extraction;

public interface IContentExtractor
{
    string[] SupportedMimeTypes { get; }
    int Priority { get; }
    Task<IReadOnlyList<ContentBlock>> ExtractAsync(
        Stream content, string mimeType, CancellationToken ct = default);
}
