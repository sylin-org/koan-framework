using System;
using Koan.Core.Modules;
using Koan.Web.Sse.Infrastructure;
using Koan.Web.Sse.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Sse.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanSse(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddKoanOptions<KoanSseOptions>(Constants.Configuration.Section);
        return services;
    }
}
