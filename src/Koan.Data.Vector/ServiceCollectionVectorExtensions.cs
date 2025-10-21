using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Infrastructure;

namespace Koan.Data.Vector;

public static class ServiceCollectionVectorExtensions
{
    public static IServiceCollection AddKoanDataVector(this IServiceCollection services)
    {
        services.TryAddSingleton<IVectorService, VectorService>();
        services.AddKoanOptions<VectorDefaultsOptions>(Constants.Configuration.DefaultsSection);
        services.AddKoanOptions<VectorWorkflowOptions>(Constants.Configuration.WorkflowsSection);
        services.TryAddSingleton<IVectorWorkflowRegistry, VectorWorkflowRegistry>();
        return services;
    }
}