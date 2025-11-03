using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Modules;

namespace Koan.Data.Relational.Orchestration;

public static class RelationalOrchestrationRegistration
{
    public static IServiceCollection AddRelationalOrchestration(this IServiceCollection services)
    {
        // Standardize options path/validation; no config path yet (defaults + configurator apply)
        services.AddKoanOptions<RelationalMaterializationOptions, RelationalMaterializationOptionsConfigurator>(
            configPath: null,
            configuratorLifetime: ServiceLifetime.Transient);
        services.TryAddSingleton<IRelationalSchemaOrchestrator, RelationalSchemaOrchestrator>();
        return services;
    }
}