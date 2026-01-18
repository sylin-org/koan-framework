using Koan.Web.Json.Strict.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Koan.Web.Json.Strict.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanMinimalJsonStrict(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>, KoanMinimalJsonOptionsConfigurator>());
        return services;
    }
}
