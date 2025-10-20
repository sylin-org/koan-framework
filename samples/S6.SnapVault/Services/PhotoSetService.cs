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
    /// Get existing session or create new one based on definition
    /// Deduplicates identical queries to avoid redundant sessions
    /// </summary>
    public async Task<PhotoSetSession> GetOrCreateSessionAsync(
        PhotoSetDefinition definition,
        CancellationToken ct = default)
    {
        // Check for existing identical session
        var existing = await FindExistingSessionAsync(definition, ct);
        if (existing != null)
        {
            _logger.LogInformation(
                "[PhotoSetService] Reusing existing session {SessionId} for context={Context}",
                existing.Id, definition.Context);

            // Update access time
            existing.LastAccessedAt = DateTimeOffset.UtcNow;
            existing.ViewCount++;
            await existing.Save(ct);

            return existing;
        }

        // Create new session
        _logger.LogInformation(
            "[PhotoSetService] Creating new session for context={Context}, query={Query}",
            definition.Context, definition.SearchQuery ?? "(none)");

        var session = await CreateSessionAsync(definition, ct);
        return session;
    }

    /// <summary>
    /// Find existing session with identical definition
    /// Prevents duplicate sessions for same query/filter combination
    /// </summary>
    public async Task<PhotoSetSession?> FindExistingSessionAsync(
        PhotoSetDefinition definition,
        CancellationToken ct = default)
    {
        // Query all sessions matching context
        var sessions = await PhotoSetSession.Query(
            s => s.Context == definition.Context &&
                 s.SortBy == definition.SortBy &&
                 s.SortOrder == definition.SortOrder,
            ct);

        // Filter by context-specific criteria
        foreach (var session in sessions)
        {
            if (IsMatchingSession(session, definition))
            {
                return session;
            }
        }

        return null;
    }

    /// <summary>
    /// Create new session from definition
    /// Executes query and captures photo ID snapshot
    /// </summary>
    private async Task<PhotoSetSession> CreateSessionAsync(
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
            CreatedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow,
            ViewCount = 1
        };

        // Auto-generate description
        session.Description = GenerateDescription(definition);

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
    /// Refresh session with current photo set
    /// Rebuilds PhotoIds snapshot with latest data
    /// </summary>
    public async Task<PhotoSetSession> RefreshSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var session = await PhotoSetSession.Get(sessionId, ct);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session {sessionId} not found");
        }

        _logger.LogInformation("[PhotoSetService] Refreshing session {SessionId}", sessionId);

        // Rebuild photo list with current data
        var definition = new PhotoSetDefinition
        {
            Context = session.Context,
            SearchQuery = session.SearchQuery,
            SearchAlpha = session.SearchAlpha,
            CollectionId = session.CollectionId,
            SortBy = session.SortBy,
            SortOrder = session.SortOrder
        };

        var photoIds = await BuildPhotoListAsync(definition, ct);

        // Update session
        session.PhotoIds = photoIds;
        session.TotalCount = photoIds.Count;
        session.LastAccessedAt = DateTimeOffset.UtcNow;

        await session.Save(ct);

        _logger.LogInformation(
            "[PhotoSetService] Refreshed session {SessionId}, now {Count} photos",
            sessionId, session.TotalCount);

        return session;
    }

    /// <summary>
    /// Check if session matches definition
    /// Used for session deduplication
    /// </summary>
    private bool IsMatchingSession(PhotoSetSession session, PhotoSetDefinition definition)
    {
        switch (definition.Context)
        {
            case "all-photos":
            case "favorites":
                return true; // No additional criteria

            case "collection":
                return session.CollectionId == definition.CollectionId;

            case "search":
                return session.SearchQuery == definition.SearchQuery &&
                       Math.Abs((session.SearchAlpha ?? 0.5) - (definition.SearchAlpha ?? 0.5)) < 0.01;

            default:
                return false;
        }
    }

    /// <summary>
    /// Generate auto description for session
    /// </summary>
    private string GenerateDescription(PhotoSetDefinition definition)
    {
        return definition.Context switch
        {
            "all-photos" => "All Photos",
            "favorites" => "Favorite Photos",
            "collection" => $"Collection: {definition.CollectionId}",
            "search" => $"Search: \"{definition.SearchQuery}\" (α={definition.SearchAlpha:F2})",
            _ => definition.Context
        };
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
