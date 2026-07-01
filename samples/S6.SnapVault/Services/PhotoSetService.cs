using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using S6.SnapVault.Models;

namespace S6.SnapVault.Services;

/// <summary>
/// PhotoSet session management — on-demand query execution, no id snapshots (scales to millions of photos). A
/// session stores the query definition; each range request re-materializes just that window. Ported verbatim from
/// the legacy service; reads inherit the SEC-0008 access axis + tenant isolation (a studio operator is
/// unconstrained within their tenant; a guest would be event-scoped).
/// </summary>
public sealed class PhotoSetService
{
    private readonly IPhotoProcessingService _processingService;
    private readonly ILogger<PhotoSetService> _logger;

    public PhotoSetService(IPhotoProcessingService processingService, ILogger<PhotoSetService> logger)
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
        => ExecuteQueryWithPagination(session.Context, session.CollectionId, session.SearchQuery,
            session.SearchAlpha ?? 0.5, session.SortBy, session.SortOrder, startIndex, count, ct);

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

            default:
                throw new ArgumentException($"Unknown context: {definition.Context}");
        }
    }

    private async Task<List<PhotoAsset>> ExecuteQueryWithPagination(
        string context, string? collectionId, string? searchQuery, double searchAlpha,
        string sortBy, string sortOrder, int skip, int take, CancellationToken ct)
    {
        switch (context)
        {
            // Fetch the (tenant + access-scoped) set, sort GLOBALLY, then window. The legacy paginated in provider
            // order then sorted only the fetched page — a per-page sort that isn't a global sort. Sample-scale
            // fetch-then-sort is correct + simple; a production version would push the sort into the query (the
            // default capturedAt??createdAt coalesce is what makes that non-trivial).
            case "all-photos":
                var allItems = (await PhotoAsset.All(ct)).ToList();
                return ApplySorting(allItems, sortBy, sortOrder).Skip(skip).Take(take).ToList();

            case "favorites":
                var favItems = (await PhotoAsset.Query(p => p.IsFavorite, ct)).ToList();
                return ApplySorting(favItems, sortBy, sortOrder).Skip(skip).Take(take).ToList();

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
                var searchResults = await _processingService.SemanticSearch(searchQuery, null, searchAlpha, skip + take, ct);
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
