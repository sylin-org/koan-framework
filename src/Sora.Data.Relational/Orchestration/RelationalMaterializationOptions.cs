using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;

namespace Sora.Data.Relational.Orchestration;

public enum RelationalMaterializationPolicy { None, ComputedProjections, PhysicalColumns }
public enum RelationalDdlPolicy { NoDdl, Validate, AutoCreate }
public enum RelationalSchemaMatchingMode { Relaxed, Strict }

public sealed class RelationalMaterializationOptions
{
    public RelationalMaterializationPolicy Materialization { get; set; } = RelationalMaterializationPolicy.None;
    public bool ProbeOnStartup { get; set; } = !SoraEnv.IsProduction;
    public bool FailOnMismatch { get; set; } = false; // escalated based on Materialization by configurator
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;
}

internal sealed class RelationalMaterializationOptionsConfigurator(IConfiguration cfg) : IConfigureOptions<RelationalMaterializationOptions>
{
    public void Configure(RelationalMaterializationOptions options)
    {
        // Bind loosely from multiple potential keys; default values already set above.
        var section = cfg.GetSection("Sora:Data:Relational:Materialization");
        section.Bind(options);
        // Sensible default: when policy != None, fail on mismatch by default.
        if (options.Materialization != RelationalMaterializationPolicy.None && !options.FailOnMismatch)
            options.FailOnMismatch = true;
        // Production safety gate
        var allowMagic = Sora.Core.Configuration.Read(cfg, Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        options.AllowProductionDdl = options.AllowProductionDdl || allowMagic;
    }
}

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
