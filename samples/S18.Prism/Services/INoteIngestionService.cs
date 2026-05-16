using S18.Prism.Models;

namespace S18.Prism.Services;

public interface INoteIngestionService
{
    Task<Note> IngestFile(Stream content, string fileName, string? contentType, string spaceId, CancellationToken ct = default);
    Task<Note> IngestUrl(string url, string spaceId, CancellationToken ct = default);
    Task<Note> IngestText(string text, string? title, string spaceId, CancellationToken ct = default);
}
