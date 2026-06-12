using Koan.Core.Adapters.Configuration;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Extensions;

/// <summary>
/// Paging math shared by adapters: Page/PageSize → Skip/Take against a <see cref="QueryDefinition"/>.
/// Page-size capping is not the data layer's concern — output-layer limits (e.g. Koan.Web's
/// PaginationSafetyBounds) are the right boundary.
/// </summary>
public static class QueryExtensions
{
    /// <summary>Converts a definition's pagination to (Skip, Take). Returns (0, int.MaxValue) when unpaged.</summary>
    public static (int Skip, int Take) ToSkipTake(this QueryDefinition? query, int defaultPageSize = 20)
    {
        var hasPagination = query?.HasPagination ?? false;
        if (!hasPagination) return (0, int.MaxValue);

        var pageSize = query!.PageSize ?? defaultPageSize;
        var page = query.Page ?? 1;
        return ((page - 1) * pageSize, pageSize);
    }

    /// <summary>Applies paging to any provider query type via a provider-specific skip/take delegate.</summary>
    public static TProviderQuery ApplyPaging<TProviderQuery>(
        this TProviderQuery query,
        QueryDefinition? definition,
        int defaultPageSize,
        Func<TProviderQuery, int, int, TProviderQuery> applySkipTake)
    {
        var (skip, take) = definition.ToSkipTake(defaultPageSize);
        return applySkipTake(query, skip, take);
    }

    public static int GetDefaultPageSize(this IAdapterOptions options) => options.DefaultPageSize;

    public static bool ShouldUseDefaultPaging(this QueryDefinition? query)
        => query is null || query.Page is null or <= 0 || query.PageSize is null or <= 0;

    public static int CalculateSkip(this QueryDefinition? query)
    {
        if (query?.Page is null or <= 0 || query.PageSize is null or <= 0) return 0;
        return (query.Page.Value - 1) * query.PageSize.Value;
    }

    public static int CalculateTake(this QueryDefinition? query, int defaultPageSize = 20)
    {
        if (query?.PageSize is null or <= 0) return defaultPageSize;
        return query.PageSize.Value;
    }
}
