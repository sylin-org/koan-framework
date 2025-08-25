using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Modules;

namespace Sora.Data.Relational.Orchestration;

public static class RelationalOrchestrationRegistration
{
    public static IServiceCollection AddRelationalOrchestration(this IServiceCollection services)
    {
        // Standardize options path/validation; no config path yet (defaults + configurator apply)
        services.AddSoraOptions<RelationalMaterializationOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, RelationalMaterializationOptionsConfigurator>());
        services.TryAddSingleton<IRelationalSchemaOrchestrator, RelationalSchemaOrchestrator>();
        return services;
    }
}