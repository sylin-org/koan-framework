using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using SnapVault.Models;

namespace SnapVault.Services;

/// <summary>
/// On-demand photo-set sessions store a query definition rather than an id snapshot. Reads inherit tenant and
/// access isolation: an operator is unconstrained within one studio, while a guest is event-scoped.
/// </summary>
public sealed class PhotoSetService
{
    private readonly PhotoProcessingService _processingService;
    private readonly ILogger<PhotoSetService> _logger;

    public PhotoSetService(PhotoProcessingService processingService, ILogger<PhotoSetService> logger)
    {
        _processingService = processingService;
        _logger = logger;
    }

    /// <summary>Create a session from a definition: compute the total count once, store the query params.</summary>
    public async Task<PhotoSetSession> CreateSession(PhotoSetDefinition definition, CancellationToken ct = default)
    {
        var totalCount = await ComputeTotalCount(definition, ct);
        var session = new PhotoSetSession
        {
            Context = definition.Context,
            SearchQuery = definition.SearchQuery,
            SearchAlpha = definition.SearchAlpha,
            CollectionId = definition.CollectionId,
            EventId = definition.EventId,
            SortBy = definition.SortBy,
            SortOrder = definition.SortOrder,
            TotalCount = totalCount,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await session.Save(ct);
        _logger.LogInformation("[PhotoSetService] Created session {SessionId} ({Context}, {TotalCount} photos)", session.Id, session.Context, totalCount);
        return session;
    }

    /// <summary>Materialize a specific range using the session's stored query definition.</summary>
    public Task<List<PhotoAsset>> ExecuteQuery(PhotoSetSession session, int startIndex, int count, CancellationToken ct = default)
        => ExecuteQueryWithPagination(session.Context, session.CollectionId, session.EventId, session.SearchQuery,
            session.SearchAlpha ?? 0.5, session.SortBy, session.SortOrder, startIndex, count, ct);

    /// <summary>Materialize the full ordered context used to locate a photo for lightbox navigation.</summary>
    public Task<List<PhotoAsset>> MaterializeContext(PhotoSetDefinition def, CancellationToken ct = default)
        => ExecuteQueryWithPagination(def.Context, def.CollectionId, def.EventId, def.SearchQuery,
            def.SearchAlpha ?? 0.5, def.SortBy, def.SortOrder, 0, int.MaxValue, ct);

    private async Task<int> ComputeTotalCount(PhotoSetDefinition definition, CancellationToken ct)
    {
        switch (definition.Context)
        {
            case "all-photos":
                var allResult = await PhotoAsset.AllWithCount(QueryDefinition.All.WithPagination(1, 1), ct);
                return (int)allResult.TotalCount;

            case "favorites":
                var favResult = await PhotoAsset.QueryWithCount(p => p.IsFavorite, QueryDefinition.All.WithPagination(1, 1), ct);
                return (int)favResult.TotalCount;

            case "collection":
                if (string.IsNullOrEmpty(definition.CollectionId))
                    throw new ArgumentException("CollectionId required for collection context");
                var collection = await Collection.Get(definition.CollectionId, ct);
                if (collection == null)
                    throw new KeyNotFoundException($"Collection {definition.CollectionId} not found");
                if (collection.PhotoIds.Count == 0) return 0;
                var collResult = await PhotoAsset.QueryWithCount(p => collection.PhotoIds.Contains(p.Id), QueryDefinition.All.WithPagination(1, 1), ct);
                return (int)collResult.TotalCount;

            case "search":
                if (string.IsNullOrEmpty(definition.SearchQuery))
                    throw new ArgumentException("SearchQuery required for search context");
                var searchResults = await _processingService.SemanticSearch(definition.SearchQuery, null, definition.SearchAlpha ?? 0.5, int.MaxValue, ct);
                return searchResults.Count;

            case "event":
                if (string.IsNullOrEmpty(definition.EventId))
                    throw new ArgumentException("EventId required for event context");
                var eventResult = await PhotoAsset.QueryWithCount(p => p.EventId == definition.EventId, QueryDefinition.All.WithPagination(1, 1), ct);
                return (int)eventResult.TotalCount;

            default:
                throw new ArgumentException($"Unknown context: {definition.Context}");
        }
    }

    private async Task<List<PhotoAsset>> ExecuteQueryWithPagination(
        string context, string? collectionId, string? eventId, string? searchQuery, double searchAlpha,
        string sortBy, string sortOrder, int skip, int take, CancellationToken ct)
    {
        switch (context)
        {
            // Fetch the tenant- and access-scoped set, sort globally, then window. Sample-scale fetch-then-sort is
            // correct and simple; a production version would push the sort into the query (the
            // default capturedAt??createdAt coalesce is what makes that non-trivial).
            case "all-photos":
                var allItems = (await PhotoAsset.All(ct)).ToList();
                return ApplySorting(allItems, sortBy, sortOrder).Skip(skip).Take(take).ToList();

            case "favorites":
                var favItems = (await PhotoAsset.Query(p => p.IsFavorite, ct)).ToList();
                return ApplySorting(favItems, sortBy, sortOrder).Skip(skip).Take(take).ToList();

            case "event":
                if (string.IsNullOrEmpty(eventId))
                    throw new ArgumentException("EventId required for event context");
                var eventItems = (await PhotoAsset.Query(p => p.EventId == eventId, ct)).ToList();
                return ApplySorting(eventItems, sortBy, sortOrder).Skip(skip).Take(take).ToList();

            case "collection":
                if (string.IsNullOrEmpty(collectionId))
                    throw new ArgumentException("CollectionId required for collection context");
                var collection = await Collection.Get(collectionId, ct);
                if (collection == null || collection.PhotoIds.Count == 0)
                    return new List<PhotoAsset>();
                // Page the id list in memory, batch-fetch, drop nulls (deleted-but-still-in-collection).
                var pageIds = collection.PhotoIds.Skip(skip).Take(take).ToList();
                if (pageIds.Count == 0) return new List<PhotoAsset>();
                var photosWithNulls = await PhotoAsset.Get(pageIds, ct);
                // Early return — preserve the collection's manual PhotoIds order (no sorting).
                return photosWithNulls.Where(p => p != null).Select(p => p!).ToList();

            case "search":
                if (string.IsNullOrEmpty(searchQuery))
                    throw new ArgumentException("SearchQuery required for search context");
                // Overflow-safe topK (skip+take can exceed int.MaxValue when MaterializeContext passes take=MaxValue).
                var topK = (int)Math.Min((long)skip + take, int.MaxValue);
                var searchResults = await _processingService.SemanticSearch(searchQuery, null, searchAlpha, topK, ct);
                // Search is already relevance-sorted.
                return searchResults.Skip(skip).Take(take).ToList();

            default:
                throw new ArgumentException($"Unknown context: {context}");
        }
    }

    private static List<PhotoAsset> ApplySorting(List<PhotoAsset> photos, string sortBy, string sortOrder)
    {
        var asc = sortOrder == "asc";
        IOrderedEnumerable<PhotoAsset> ordered = sortBy switch
        {
            "createdAt" => asc ? photos.OrderBy(p => p.CreatedAt) : photos.OrderByDescending(p => p.CreatedAt),
            "rating" => asc ? photos.OrderBy(p => p.Rating) : photos.OrderByDescending(p => p.Rating),
            "fileName" => asc ? photos.OrderBy(p => p.OriginalFileName) : photos.OrderByDescending(p => p.OriginalFileName),
            _ => asc ? photos.OrderBy(p => p.CapturedAt ?? p.CreatedAt) : photos.OrderByDescending(p => p.CapturedAt ?? p.CreatedAt),   // capturedAt (default)
        };
        return ordered.ToList();
    }
}
