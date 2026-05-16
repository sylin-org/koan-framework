using System;
using Koan.Core.Adapters.Configuration;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Extensions;

/// <summary>
/// Extension methods for standardizing query operations across data adapters.
/// Centralizes Page/PageSize to Skip/Take conversion. Note: page-size capping is no longer
/// the data layer's concern — adapters honour the requested page size as-is. Output-layer
/// limits (e.g. <c>Koan.Web</c>'s <c>PaginationSafetyBounds</c>) are the right boundary
/// for protecting against accidental megapacket responses.
/// </summary>
public static class QueryExtensions
{
    /// <summary>
    /// Converts Page/PageSize options to Skip/Take values for provider-specific queries.
    /// <see cref="DefaultPageSize"/> is only used when pagination IS active but PageSize isn't specified.
    /// </summary>
    /// <param name="options">The data query options containing page and page size</param>
    /// <param name="defaultPageSize">Default page size when pagination is active but PageSize not specified</param>
    /// <returns>Skip and Take values for use with provider queries</returns>
    public static (int Skip, int Take) ToSkipTake(
        this DataQueryOptions? options,
        int defaultPageSize = 20)
    {
        // Check if pagination is active
        var hasPagination = options?.HasPagination ?? false;

        if (!hasPagination)
        {
            // No pagination - return full result set
            return (0, int.MaxValue);
        }

        // Pagination is active - apply default when PageSize is missing.
        var pageSize = options?.PageSize ?? defaultPageSize;
        var page = options?.Page ?? 1;
        var skip = (page - 1) * pageSize;
        return (skip, pageSize);
    }

    /// <summary>
    /// Generic paging application for any provider query type.
    /// Applies skip/take logic through a provider-specific delegate.
    /// </summary>
    /// <typeparam name="TProviderQuery">The provider-specific query type</typeparam>
    /// <param name="query">The provider query to apply paging to</param>
    /// <param name="options">The data query options</param>
    /// <param name="defaultPageSize">Default page size when not specified</param>
    /// <param name="applySkipTake">Provider-specific function to apply skip/take values</param>
    /// <returns>The modified provider query with paging applied</returns>
    public static TProviderQuery ApplyPaging<TProviderQuery>(
        this TProviderQuery query,
        DataQueryOptions? options,
        int defaultPageSize,
        Func<TProviderQuery, int, int, TProviderQuery> applySkipTake)
    {
        var (skip, take) = options.ToSkipTake(defaultPageSize);
        return applySkipTake(query, skip, take);
    }

    /// <summary>
    /// Provider-agnostic paging-default extraction from adapter options. Returns the
    /// configured <see cref="IAdapterOptions.DefaultPageSize"/>; max-page caps are no longer
    /// part of the adapter contract.
    /// </summary>
    /// <param name="options">The adapter options containing paging configuration</param>
    /// <returns>Default page size for use in query operations</returns>
    public static int GetDefaultPageSize(this IAdapterOptions options)
        => options.DefaultPageSize;

    /// <summary>
    /// Determines if a query should use default paging based on options.
    /// Helps providers decide when to apply fallback page size behavior.
    /// </summary>
    /// <param name="options">The data query options to evaluate</param>
    /// <returns>True if default paging should be applied</returns>
    public static bool ShouldUseDefaultPaging(this DataQueryOptions? options)
        => options is null || options.Page is null or <= 0 || options.PageSize is null or <= 0;

    /// <summary>
    /// Calculates the total number of items to skip for pagination.
    /// </summary>
    /// <param name="options">The data query options</param>
    /// <returns>Number of items to skip, or 0 if invalid options</returns>
    public static int CalculateSkip(this DataQueryOptions? options)
    {
        if (options?.Page is null or <= 0 || options.PageSize is null or <= 0)
            return 0;
        return (options.Page.Value - 1) * options.PageSize.Value;
    }

    /// <summary>
    /// Calculates the number of items to take for pagination.
    /// </summary>
    /// <param name="options">The data query options</param>
    /// <param name="defaultPageSize">Default page size when not specified</param>
    /// <returns>Number of items to take</returns>
    public static int CalculateTake(this DataQueryOptions? options, int defaultPageSize = 20)
    {
        if (options?.PageSize is null or <= 0)
            return defaultPageSize;
        return options.PageSize.Value;
    }
}
