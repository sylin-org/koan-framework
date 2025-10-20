using Koan.Data.Abstractions;
using Koan.Data.Core;
using S6.SnapVault.Models;

namespace S6.SnapVault.Services;

/// <summary>
/// PhotoSet Session Management Service
/// Handles creation, retrieval, and lifecycle of PhotoSet sessions
/// Uses Koan Entity patterns for provider-agnostic storage
/// </summary>
public sealed class PhotoSetService
{
    private readonly IPhotoProcessingService _processingService;
    private readonly ILogger<PhotoSetService> _logger;

    public PhotoSetService(
        IPhotoProcessingService processingService,
        ILogger<PhotoSetService> logger)
    {
        _processingService = processingService;
        _logger = logger;
    }

    /// <summary>
    /// Create new session from definition
    /// Always creates fresh session - no deduplication
    /// </summary>
    public async Task<PhotoSetSession> CreateSessionAsync(
        PhotoSetDefinition definition,
        CancellationToken ct = default)
    {
        // Build photo list based on definition
        var photoIds = await BuildPhotoListAsync(definition, ct);

        // Create session entity
        var session = new PhotoSetSession
        {
            Context = definition.Context,
            SearchQuery = definition.SearchQuery,
            SearchAlpha = definition.SearchAlpha,
            CollectionId = definition.CollectionId,
            SortBy = definition.SortBy,
            SortOrder = definition.SortOrder,
            PhotoIds = photoIds,
            TotalCount = photoIds.Count,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Save using Koan Entity pattern
        await session.Save(ct);

        _logger.LogInformation(
            "[PhotoSetService] Created session {SessionId} with {Count} photos",
            session.Id, session.TotalCount);

        return session;
    }

    /// <summary>
    /// Build list of photo IDs based on definition
    /// Reuses PhotosController query logic
    /// </summary>
    public async Task<List<string>> BuildPhotoListAsync(
        PhotoSetDefinition definition,
        CancellationToken ct = default)
    {
        List<PhotoAsset> photos;

        // Use large page size to get ALL records (bypass default 50-record limit)
        // Sessions need complete photo list for consistent navigation
        // Note: Will be capped by provider's maxPageSize (typically 1000)
        var options = new DataQueryOptions(page: 1, pageSize: 10000);

        switch (definition.Context)
        {
            case "all-photos":
                var allResult = await PhotoAsset.AllWithCount(options, ct);
                photos = allResult.Items.ToList();
                break;

            case "favorites":
                var favoritesResult = await PhotoAsset.QueryWithCount(p => p.IsFavorite, options, ct);
                photos = favoritesResult.Items.ToList();
                break;

            case "collection":
                if (string.IsNullOrEmpty(definition.CollectionId))
                {
                    throw new ArgumentException("CollectionId required for collection context");
                }

                // Get collection and its photo IDs
                var collection = await Collection.Get(definition.CollectionId, ct);
                if (collection == null)
                {
                    throw new KeyNotFoundException($"Collection {definition.CollectionId} not found");
                }

                // Query photos by IDs from collection
                if (collection.PhotoIds.Count > 0)
                {
                    var collectionResult = await PhotoAsset.QueryWithCount(
                        p => collection.PhotoIds.Contains(p.Id),
                        options,
                        ct);
                    photos = collectionResult.Items.ToList();
                }
                else
                {
                    photos = new List<PhotoAsset>();
                }
                break;

            case "search":
                if (string.IsNullOrEmpty(definition.SearchQuery))
                {
                    throw new ArgumentException("SearchQuery required for search context");
                }

                // Use semantic search service
                photos = await _processingService.SemanticSearchAsync(
                    query: definition.SearchQuery,
                    eventId: null,
                    alpha: definition.SearchAlpha ?? 0.5,
                    topK: int.MaxValue
                );

                // Search results are pre-sorted by relevance - return IDs directly
                return photos.Select(p => p.Id).ToList();

            default:
                throw new ArgumentException($"Unknown context: {definition.Context}");
        }

        // Apply sorting for non-search contexts
        photos = ApplySorting(photos, definition.SortBy, definition.SortOrder);

        return photos.Select(p => p.Id).ToList();
    }


    /// <summary>
    /// Apply sorting to photo list
    /// </summary>
    private List<PhotoAsset> ApplySorting(
        List<PhotoAsset> photos,
        string sortBy,
        string sortOrder)
    {
        IOrderedEnumerable<PhotoAsset> ordered = sortBy switch
        {
            "capturedAt" => sortOrder == "asc"
                ? photos.OrderBy(p => p.CapturedAt ?? p.CreatedAt)
                : photos.OrderByDescending(p => p.CapturedAt ?? p.CreatedAt),

            "createdAt" => sortOrder == "asc"
                ? photos.OrderBy(p => p.CreatedAt)
                : photos.OrderByDescending(p => p.CreatedAt),

            "rating" => sortOrder == "asc"
                ? photos.OrderBy(p => p.Rating)
                : photos.OrderByDescending(p => p.Rating),

            "fileName" => sortOrder == "asc"
                ? photos.OrderBy(p => p.OriginalFileName)
                : photos.OrderByDescending(p => p.OriginalFileName),

            _ => sortOrder == "asc"
                ? photos.OrderBy(p => p.CapturedAt ?? p.CreatedAt)
                : photos.OrderByDescending(p => p.CapturedAt ?? p.CreatedAt)
        };

        return ordered.ToList();
    }
}
