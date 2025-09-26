using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Infrastructure;

public static class PaginationServiceCollectionExtensions
{
    public static IServiceCollection AddPaginationSafetyBounds(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PaginationSafetyBounds>(configuration.GetSection("Pagination"));

        services.PostConfigure<PaginationSafetyBounds>(bounds =>
        {
            bounds.MinPageSize = Math.Max(bounds.MinPageSize, 1);
            bounds.MaxPageSize = Math.Clamp(bounds.MaxPageSize, bounds.MinPageSize, 1_000);
            bounds.AbsoluteMaxRecords = Math.Max(bounds.AbsoluteMaxRecords, bounds.MaxPageSize);
        });

        return services;
    }
}
