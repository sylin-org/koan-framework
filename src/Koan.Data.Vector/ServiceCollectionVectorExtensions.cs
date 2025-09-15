using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Modules;

namespace Koan.Data.Vector;

public static class ServiceCollectionVectorExtensions
{
    public static IServiceCollection AddKoanDataVector(this IServiceCollection services)
    {
        services.TryAddSingleton<IVectorService, VectorService>();
        services.AddKoanOptions<VectorDefaultsOptions>("Koan:Data:VectorDefaults");
        return services;
    }
}