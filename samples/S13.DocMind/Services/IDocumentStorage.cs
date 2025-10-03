using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentStorage
{
    Task<StoredDocumentDescriptor> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(DocumentStorageLocation location, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(DocumentStorageLocation location, CancellationToken cancellationToken);
    Task<bool> TryDeleteAsync(DocumentStorageLocation location, CancellationToken cancellationToken);
}

public sealed class StoredDocumentDescriptor
{
    public string Provider { get; init; } = "local";
    public string Bucket { get; init; } = "local";
    public string ObjectKey { get; init; } = string.Empty;
    public string? VersionId { get; init; }
        = null;
    public string ProviderPath { get; init; } = string.Empty;
    public long Length { get; init; }
    public string? Hash { get; init; }

    public DocumentStorageLocation ToLocation()
        => new()
        {
            Provider = Provider,
            Bucket = Bucket,
            ObjectKey = ObjectKey,
            VersionId = VersionId,
            ProviderPath = ProviderPath
        };
}
