using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;

namespace Koan.Recipe.Abstractions;

internal sealed class RecipeApplier : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Try to fetch IConfiguration and IHostEnvironment without building a provider
        var cfg = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration))?.ImplementationInstance as IConfiguration
                  ?? new ConfigurationBuilder().Build();
        var env = services.FirstOrDefault(d => d.ServiceType == typeof(IHostEnvironment))?.ImplementationInstance as IHostEnvironment
                  ?? new HostingEnvironment { EnvironmentName = Environments.Production };
        // Use a safe logger; avoid building a provider. If a real provider is later added, these logs won't duplicate.
        var logger = (ILogger)NullLogger.Instance;

        var opts = cfg.GetSection("Koan:Recipes").Get<KoanRecipeOptions>() ?? new KoanRecipeOptions();
        var active = opts.Active?.Length > 0 ? new HashSet<string>(opts.Active, StringComparer.OrdinalIgnoreCase) : null;

        var recipeTypes = RecipeRegistry.GetRegistered()
            .Select(t => (Type: t, Instance: Activator.CreateInstance(t) as IKoanRecipe))
            .Where(t => t.Instance is not null)
            .Select(t => t.Instance!)
            .OrderBy(r => r.Order)
            .ToList();

        foreach (var r in recipeTypes)
        {
            if (active is not null && !active.Contains(r.Name))
            {
                logger.LogInformation(RecipeLog.Events.SkippedNotActive, "Skip recipe {Name}: not in active list", r.Name);
                continue;
            }
            if (!r.ShouldApply(cfg, env))
            {
                logger.LogInformation(RecipeLog.Events.SkippedShouldApplyFalse, "Skip recipe {Name}: ShouldApply=false", r.Name);
                continue;
            }
            if (opts.DryRun)
            {
                logger.LogInformation(RecipeLog.Events.DryRun, "DryRun: would apply recipe {Name}", r.Name);
                continue;
            }
            try
            {
                logger.LogInformation(RecipeLog.Events.Applying, "Apply recipe {Name} (Order {Order})", r.Name, r.Order);
                r.Apply(services, cfg, env);
                logger.LogInformation(RecipeLog.Events.AppliedOk, "Applied recipe {Name}", r.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(RecipeLog.Events.ApplyFailed, ex, "Recipe {Name} threw during Apply; continuing", r.Name);
            }
        }
    }
}