namespace S6.SnapVault.Services;

using S6.SnapVault.Models;

/// <summary>
/// Service for processing uploaded photos: resize, extract EXIF, generate AI metadata, vector embeddings
/// </summary>
public interface IPhotoProcessingService
{
    /// <summary>
    /// Process a single uploaded photo: storage, derivatives, EXIF, AI analysis
    /// If eventId is null, auto-creates daily event based on EXIF capture date
    /// Emits SignalR events to notify clients of processing progress
    /// </summary>
    Task<PhotoAsset> ProcessUploadAsync(string? eventId, IFormFile file, string jobId, CancellationToken ct = default);

    /// <summary>
    /// Generate AI metadata and vector embedding for a photo
    /// </summary>
    Task<PhotoAsset> GenerateAIMetadataAsync(PhotoAsset photo, CancellationToken ct = default);

    /// <summary>
    /// Semantic search across photos using natural language query with hybrid search control
    /// </summary>
    /// <param name="alpha">Search mode: 0.0 = pure keyword, 1.0 = pure semantic, 0.5 = hybrid</param>
    Task<List<PhotoAsset>> SemanticSearchAsync(string query, string? eventId = null, double alpha = 0.5, int topK = 20, CancellationToken ct = default);

    /// <summary>
    /// Regenerate AI analysis for a photo while preserving locked facts
    /// Used for "reroll with holds" mechanic - users can lock specific facts and reroll the rest
    /// </summary>
    /// <param name="photoId">Photo ID to regenerate analysis for</param>
    /// <param name="analysisStyle">Optional analysis style (smart, portrait, product, etc.). If null, uses last style or default.</param>
    /// <param name="ct">Cancellation token</param>
    Task<PhotoAsset> RegenerateAIAnalysisAsync(string photoId, string? analysisStyle = null, CancellationToken ct = default);
}
