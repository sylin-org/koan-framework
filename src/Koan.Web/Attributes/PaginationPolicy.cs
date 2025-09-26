using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Web.Infrastructure;

namespace Koan.Web.Attributes;

public sealed record PaginationPolicy
{
    public required PaginationMode Mode { get; init; }
    public required int DefaultSize { get; init; }
    public required int MaxSize { get; init; }
    public required bool IncludeCount { get; init; }
    public required int AbsoluteMaxRecords { get; init; }
    public string? DefaultSort { get; init; }

    public static PaginationPolicy FromAttribute(PaginationAttribute attr, PaginationSafetyBounds safety)
    {
        if (attr is null) throw new ArgumentNullException(nameof(attr));
        if (safety is null) throw new ArgumentNullException(nameof(safety));

        var defaultSize = Math.Clamp(attr.DefaultSize, safety.MinPageSize, safety.MaxPageSize);
        var maxSize = Math.Clamp(attr.MaxSize, safety.MinPageSize, safety.MaxPageSize);
        if (maxSize < defaultSize)
        {
            maxSize = defaultSize;
        }

        return new PaginationPolicy
        {
            Mode = attr.Mode,
            DefaultSize = defaultSize,
            MaxSize = maxSize,
            IncludeCount = attr.IncludeCount,
            AbsoluteMaxRecords = safety.AbsoluteMaxRecords,
            DefaultSort = attr.DefaultSort
        };
    }

    public static PaginationPolicy Resolve(IServiceProvider services, PaginationAttribute? attribute)
    {
        var bounds = services.GetService<IOptions<PaginationSafetyBounds>>()?.Value
                     ?? PaginationSafetyBounds.Default;
        return FromAttribute(attribute ?? PaginationAttributeDefaults.CreateDefault(), bounds);
    }
}

internal static class PaginationAttributeDefaults
{
    public static PaginationAttribute CreateDefault()
        => new()
        {
            Mode = PaginationMode.On,
            DefaultSize = Infrastructure.KoanWebConstants.Defaults.DefaultPageSize,
            MaxSize = Infrastructure.KoanWebConstants.Defaults.MaxPageSize,
            IncludeCount = true
        };
}
