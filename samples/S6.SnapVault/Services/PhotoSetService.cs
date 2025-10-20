using Koan.Data.Abstractions;
using Koan.Data.Core;
using S6.SnapVault.Models;

namespace S6.SnapVault.Services;

/// <summary>
/// PhotoSet Session Management Service
/// On-demand query execution - no ID snapshots, infinite scalability
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
    /// Computes total count, stores query params for on-demand execution
    /// </summary>
    public async Task<PhotoSetSession> CreateSessionAsync(
        PhotoSetDefinition definition,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[PhotoSetService] Creating session for context: {Context}", definition.Context);

        // Compute total count
        int totalCount = await ComputeTotalCountAsync(definition, ct);

        // Create session with query definition
        var session = new PhotoSetSession
        {
            Context = definition.Context,
            SearchQuery = definition.SearchQuery,
            SearchAlpha = definition.SearchAlpha,
            CollectionId = definition.CollectionId,
            SortBy = definition.SortBy,
            SortOrder = definition.SortOrder,
            TotalCount = totalCount,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await session.Save(ct);

        _logger.LogInformation(
            "[PhotoSetService] Created session {SessionId} with {TotalCount} photos",
            session.Id, totalCount);

        return session;
    }

    /// <summary>
    /// Execute query for specific range using session's query definition
    /// On-demand materialization - scales to millions of photos
    /// </summary>
    public async Task<List<PhotoAsset>> ExecuteQueryAsync(
        PhotoSetSession session,
        int startIndex,
        int count,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PhotoSetService] Executing query for session {SessionId}: range [{Start}, {End}]",
            session.Id, startIndex, startIndex + count);

        // Execute query with pagination
        var photos = await ExecuteQueryWithPaginationAsync(
            session.Context,
            session.CollectionId,
            session.SearchQuery,
            session.SearchAlpha ?? 0.5,
            session.SortBy,
            session.SortOrder,
            startIndex,
            count,
            ct);

        _logger.LogInformation(
            "[PhotoSetService] Materialized {Count} photos for range [{Start}, {End}]",
            photos.Count, startIndex, startIndex + count);

        return photos;
    }

    /// <summary>
    /// Compute total count for a query definition
    /// </summary>
    private async Task<int> ComputeTotalCountAsync(
        PhotoSetDefinition definition,
        CancellationToken ct)
    {
        switch (definition.Context)
        {
            case "all-photos":
                var allResult = await PhotoAsset.AllWithCount(new DataQueryOptions(page: 1, pageSize: 1), ct);
                return (int)allResult.TotalCount;

            case "favorites":
                var favResult = await PhotoAsset.QueryWithCount(
                    p => p.IsFavorite,
                    new DataQueryOptions(page: 1, pageSize: 1),
                    ct);
                return (int)favResult.TotalCount;

            case "collection":
                if (string.IsNullOrEmpty(definition.CollectionId))
                {
                    throw new ArgumentException("CollectionId required for collection context");
                }

                var collection = await Collection.Get(definition.CollectionId, ct);
                if (collection == null)
                {
                    throw new KeyNotFoundException($"Collection {definition.CollectionId} not found");
                }

                if (collection.PhotoIds.Count == 0)
                {
                    return 0;
                }

                var collResult = await PhotoAsset.QueryWithCount(
                    p => collection.PhotoIds.Contains(p.Id),
                    new DataQueryOptions(page: 1, pageSize: 1),
                    ct);
                return (int)collResult.TotalCount;

            case "search":
                if (string.IsNullOrEmpty(definition.SearchQuery))
                {
                    throw new ArgumentException("SearchQuery required for search context");
                }

                // For search, we need to execute the full search to get count
                var searchResults = await _processingService.SemanticSearchAsync(
                    query: definition.SearchQuery,
                    eventId: null,
                    alpha: definition.SearchAlpha ?? 0.5,
                    topK: int.MaxValue);

                return searchResults.Count;

            default:
                throw new ArgumentException($"Unknown context: {definition.Context}");
        }
    }

    /// <summary>
    /// Execute query with pagination for specific range
    /// </summary>
    private async Task<List<PhotoAsset>> ExecuteQueryWithPaginationAsync(
        string context,
        string? collectionId,
        string? searchQuery,
        double searchAlpha,
        string sortBy,
        string sortOrder,
        int skip,
        int take,
        CancellationToken ct)
    {
        // Calculate page number for provider's pagination
        // Note: Koan uses 1-based page numbers
        int page = (skip / take) + 1;
        var options = new DataQueryOptions(page: page, pageSize: take);

        // Adjust skip for page offset
        int pageSkip = skip % take;

        List<PhotoAsset> photos;

        switch (context)
        {
            case "all-photos":
                var allResult = await PhotoAsset.AllWithCount(options, ct);
                photos = allResult.Items.Skip(pageSkip).Take(take).ToList();
                break;

            case "favorites":
                var favResult = await PhotoAsset.QueryWithCount(
                    p => p.IsFavorite,
                    options,
                    ct);
                photos = favResult.Items.Skip(pageSkip).Take(take).ToList();
                break;

            case "collection":
                if (string.IsNullOrEmpty(collectionId))
                {
                    throw new ArgumentException("CollectionId required for collection context");
                }

                var collection = await Collection.Get(collectionId, ct);
                if (collection == null || collection.PhotoIds.Count == 0)
                {
                    return new List<PhotoAsset>();
                }

                // Paginate the ID list in memory, then batch fetch those photos
                var photoIds = collection.PhotoIds;
                var pageIds = photoIds.Skip(skip).Take(take).ToList();

                if (pageIds.Count == 0)
                {
                    return new List<PhotoAsset>();
                }

                // Batch fetch photos using GetManyAsync - preserves order and returns nulls for missing
                var photosWithNulls = await PhotoAsset.Get(pageIds, ct);

                // Filter out nulls (photos that were deleted but still in collection)
                photos = photosWithNulls
                    .Where(p => p != null)
                    .Select(p => p!)
                    .ToList();

                // Early return - already in collection order (no need to sort)
                return photos;

            case "search":
                if (string.IsNullOrEmpty(searchQuery))
                {
                    throw new ArgumentException("SearchQuery required for search context");
                }

                // Search returns pre-sorted results
                var searchResults = await _processingService.SemanticSearchAsync(
                    query: searchQuery,
                    eventId: null,
                    alpha: searchAlpha,
                    topK: skip + take); // Get up to the range we need

                photos = searchResults.Skip(skip).Take(take).ToList();
                return photos; // Search is already sorted

            default:
                throw new ArgumentException($"Unknown context: {context}");
        }

        // Apply sorting for non-search contexts
        return ApplySorting(photos, sortBy, sortOrder);
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
