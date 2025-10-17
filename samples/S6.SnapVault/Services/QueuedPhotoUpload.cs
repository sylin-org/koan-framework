namespace S6.SnapVault.Services;

/// <summary>
/// Represents a photo upload queued for background processing
/// </summary>
public class QueuedPhotoUpload
{
    public required string JobId { get; init; }
    public required string? EventId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required byte[] FileData { get; init; }
    public DateTime QueuedAt { get; init; } = DateTime.UtcNow;
}
