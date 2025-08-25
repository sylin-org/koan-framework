using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sora.Data.Vector;

public static class ServiceCollectionVectorExtensions
{
    public static IServiceCollection AddSoraDataVector(this IServiceCollection services)
    {
        services.TryAddSingleton<IVectorService, VectorService>();
        services.AddOptions<VectorDefaultsOptions>().BindConfiguration("Sora:Data:VectorDefaults");
        return services;
    }
}