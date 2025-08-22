using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Sora.Data.Relational.Orchestration;

public static class RelationalOrchestrationRegistration
{
    public static IServiceCollection AddRelationalOrchestration(this IServiceCollection services)
    {
        services.AddOptions<RelationalMaterializationOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, RelationalMaterializationOptionsConfigurator>());
        services.TryAddSingleton<IRelationalSchemaOrchestrator, RelationalSchemaOrchestrator>();
        return services;
    }
}