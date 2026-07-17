using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Schema;
using Koan.Data.Vector.Infrastructure;

namespace Koan.Data.Vector;

public static class ServiceCollectionVectorExtensions
{
    public static IServiceCollection AddKoanDataVector(this IServiceCollection services)
    {
        services.TryAddSingleton<IVectorAdapterParticipation, VectorAdapterParticipation>();
        services.TryAddSingleton<IVectorService, VectorService>();
        services.TryAddSingleton<VectorProviderCatalog>(sp => new VectorProviderCatalog(
            sp.GetServices<IVectorAdapterFactory>(),
            sp.GetService<Koan.Core.Composition.KoanApplicationReferenceManifest>()));
        services.TryAddSingleton<IVectorProviderResolver>(sp => sp.GetRequiredService<VectorProviderCatalog>());
        services.AddKoanOptions<VectorDefaultsOptions>(Constants.Configuration.DefaultsSection);
        services.TryAddSingleton<VectorSchemaRegistry>();
        return services;
    }
}
