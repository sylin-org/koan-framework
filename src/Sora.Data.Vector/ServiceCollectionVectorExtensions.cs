using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Core.Modules;

namespace Sora.Data.Vector;

public static class ServiceCollectionVectorExtensions
{
    public static IServiceCollection AddSoraDataVector(this IServiceCollection services)
    {
        services.TryAddSingleton<IVectorService, VectorService>();
        services.AddSoraOptions<VectorDefaultsOptions>("Sora:Data:VectorDefaults");
        return services;
    }
}