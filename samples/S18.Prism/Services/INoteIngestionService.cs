using S18.Prism.Models;

namespace S18.Prism.Services;

public interface INoteIngestionService
{
    Task<Note> IngestFileAsync(Stream content, string fileName, string? contentType, string spaceId, CancellationToken ct = default);
    Task<Note> IngestUrlAsync(string url, string spaceId, CancellationToken ct = default);
    Task<Note> IngestTextAsync(string text, string? title, string spaceId, CancellationToken ct = default);
}
