using System;
using Koan.Core.Adapters.Configuration;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Extensions;

/// <summary>
/// Extension methods for standardizing query operations across data adapters.
/// Centralizes Page/PageSize to Skip/Take conversion and paging logic.
/// </summary>
public static class QueryExtensions
{
    /// <summary>
    /// Converts Page/PageSize options to Skip/Take values for provider-specific queries.
    /// Handles null values and enforces maximum page size limits.
    /// </summary>
    /// <param name="options">The data query options containing page and page size</param>
    /// <param name="defaultPageSize">Default page size when not specified</param>
    /// <param name="maxPageSize">Maximum allowed page size to prevent resource exhaustion</param>
    /// <returns>Skip and Take values for use with provider queries</returns>
    public static (int Skip, int Take) ToSkipTake(
        this DataQueryOptions? options,
        int defaultPageSize = 20,
        int maxPageSize = 1000)
    {
        if (options?.Page is null or <= 0 || options.PageSize is null or <= 0)
            return (0, defaultPageSize);

        var safePageSize = Math.Min(options.PageSize.Value, maxPageSize);
        var skip = (options.Page.Value - 1) * safePageSize;
        return (skip, safePageSize);
    }

    /// <summary>
    /// Generic paging application for any provider query type.
    /// Applies skip/take logic through a provider-specific delegate.
    /// </summary>
    /// <typeparam name="TProviderQuery">The provider-specific query type</typeparam>
    /// <param name="query">The provider query to apply paging to</param>
    /// <param name="options">The data query options</param>
    /// <param name="defaultPageSize">Default page size when not specified</param>
    /// <param name="maxPageSize">Maximum allowed page size</param>
    /// <param name="applySkipTake">Provider-specific function to apply skip/take values</param>
    /// <returns>The modified provider query with paging applied</returns>
    public static TProviderQuery ApplyPaging<TProviderQuery>(
        this TProviderQuery query,
        DataQueryOptions? options,
        int defaultPageSize,
        int maxPageSize,
        Func<TProviderQuery, int, int, TProviderQuery> applySkipTake)
    {
        var (skip, take) = options.ToSkipTake(defaultPageSize, maxPageSize);
        return applySkipTake(query, skip, take);
    }

    /// <summary>
    /// Provider-agnostic paging guardrails extraction from adapter options.
    /// Standardizes how adapters access their configured page size limits.
    /// </summary>
    /// <param name="options">The adapter options containing paging configuration</param>
    /// <returns>Default and maximum page sizes for use in query operations</returns>
    public static (int DefaultPageSize, int MaxPageSize) GetPagingGuardrails(
        this IAdapterOptions options)
        => (options.DefaultPageSize, options.MaxPageSize);

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
    /// Used when providers need the skip value independently.
    /// </summary>
    /// <param name="options">The data query options</param>
    /// <param name="maxPageSize">Maximum allowed page size</param>
    /// <returns>Number of items to skip, or 0 if invalid options</returns>
    public static int CalculateSkip(this DataQueryOptions? options, int maxPageSize = 1000)
    {
        if (options?.Page is null or <= 0 || options.PageSize is null or <= 0)
            return 0;

        var safePageSize = Math.Min(options.PageSize.Value, maxPageSize);
        return (options.Page.Value - 1) * safePageSize;
    }

    /// <summary>
    /// Calculates the number of items to take for pagination.
    /// Used when providers need the take value independently.
    /// </summary>
    /// <param name="options">The data query options</param>
    /// <param name="defaultPageSize">Default page size when not specified</param>
    /// <param name="maxPageSize">Maximum allowed page size</param>
    /// <returns>Number of items to take</returns>
    public static int CalculateTake(this DataQueryOptions? options, int defaultPageSize = 20, int maxPageSize = 1000)
    {
        if (options?.PageSize is null or <= 0)
            return defaultPageSize;

        return Math.Min(options.PageSize.Value, maxPageSize);
    }
}