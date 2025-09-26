namespace S13.DocMind.Services;

public interface IDocumentStorage
{
    Task<StoredDocumentLocation> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(StoredDocumentLocation location, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(StoredDocumentLocation location, CancellationToken cancellationToken);
}

public sealed class StoredDocumentLocation
{
    public string Provider { get; init; } = "local";
    public string Path { get; init; } = string.Empty;
    public long Length { get; init; }
    public string? Hash { get; init; }
}
