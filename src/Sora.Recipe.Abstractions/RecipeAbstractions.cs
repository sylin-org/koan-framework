using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Sora.Core;

namespace Sora.Recipe;

// Contract for a recipe bundle
public interface ISoraRecipe
{
    string Name { get; }
    int Order => 0;
    bool ShouldApply(IConfiguration cfg, IHostEnvironment env) => true;
    void Apply(IServiceCollection services, IConfiguration cfg, IHostEnvironment env);
}

// Assembly-level registration for AOT-safe discovery (no broad scans)
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SoraRecipeAttribute : Attribute
{
    public Type RecipeType { get; }
    public SoraRecipeAttribute(Type recipeType)
    {
        RecipeType = recipeType;
    }
}

public sealed class SoraRecipeOptions
{
    public string[] Active { get; set; } = Array.Empty<string>();
    public bool AllowOverrides { get; set; } = false;
    public bool DryRun { get; set; } = false;
    // Per-recipe flags live under Sora:Recipes:<RecipeName>:ForceOverrides
}

internal static class RecipeRegistry
{
    private static readonly List<Type> Registered = new();
    public static void Register<T>() where T : ISoraRecipe => Register(typeof(T));
    public static void Register(Type t)
    {
        if (!typeof(ISoraRecipe).IsAssignableFrom(t)) throw new ArgumentException("Not a recipe type", nameof(t));
        if (!Registered.Contains(t)) Registered.Add(t);
    }
    public static IReadOnlyList<Type> GetRegistered() => Registered.ToArray();
}

public static class SoraRecipeServiceCollectionExtensions
{
    private const string ConfigRoot = "Sora:Recipes";

    public static IServiceCollection AddRecipe<T>(this IServiceCollection services)
        where T : class, ISoraRecipe
    {
        RecipeRegistry.Register<T>();
        return services;
    }

    public static IServiceCollection AddRecipe(this IServiceCollection services, string name)
    {
        services.PostConfigure<SoraRecipeOptions>(o =>
        {
            if (!o.Active.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                var list = o.Active.ToList();
                list.Add(name);
                o.Active = list.ToArray();
            }
        });
        return services;
    }

    internal static void ApplyActiveRecipes(IServiceCollection services)
    {
        // Bind options if IConfiguration is present
        services.AddOptions<SoraRecipeOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new RecipeInitializer()));
    }
}

internal sealed class RecipeInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Read assembly-level attributes for self-registered recipes
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var attrs = asm.GetCustomAttributes(typeof(SoraRecipeAttribute), false).Cast<SoraRecipeAttribute>();
                foreach (var a in attrs) RecipeRegistry.Register(a.RecipeType);
            }
            catch { /* ignore */ }
        }

        // Defer application until a ServiceProvider exists: we need IConfiguration/IHostEnvironment
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new RecipeApplier()));
    }
}

internal sealed class RecipeApplier : ISoraInitializer
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

        var opts = cfg.GetSection("Sora:Recipes").Get<SoraRecipeOptions>() ?? new SoraRecipeOptions();
        var active = opts.Active?.Length > 0 ? new HashSet<string>(opts.Active, StringComparer.OrdinalIgnoreCase) : null;

        var recipeTypes = RecipeRegistry.GetRegistered()
            .Select(t => (Type: t, Instance: Activator.CreateInstance(t) as ISoraRecipe))
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

// Minimal hosting environment fallback
internal sealed class HostingEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "SoraApp";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

// Structured event IDs for recipe bootstrap logs
internal static class RecipeLog
{
    public static class Events
    {
        public static readonly EventId Applying = new(41000, nameof(Applying));
        public static readonly EventId AppliedOk = new(41001, nameof(AppliedOk));
        public static readonly EventId SkippedNotActive = new(41002, nameof(SkippedNotActive));
        public static readonly EventId SkippedShouldApplyFalse = new(41003, nameof(SkippedShouldApplyFalse));
        public static readonly EventId DryRun = new(41004, nameof(DryRun));
        public static readonly EventId ApplyFailed = new(41005, nameof(ApplyFailed));
    }
}

public static class RecipeGates
{
    public static bool ForcedOverridesEnabled(IConfiguration cfg, string recipeName)
    {
        var global = cfg.GetValue<bool?>("Sora:Recipes:AllowOverrides") ?? false;
        if (!global) return false;
        var per = cfg.GetValue<bool?>($"Sora:Recipes:{recipeName}:ForceOverrides") ?? false;
        return per;
    }
}

public static class RecipeOptionsExtensions
{
    public static OptionsBuilder<TOptions> WithRecipeForcedOverridesIfEnabled<TOptions>(this OptionsBuilder<TOptions> builder, IConfiguration cfg, string recipeName, Action<TOptions> postConfigure)
        where TOptions : class
    {
        if (RecipeGates.ForcedOverridesEnabled(cfg, recipeName))
        {
            builder.Services.PostConfigure(postConfigure);
        }
        return builder;
    }
}

// Optional capability gating helpers recipes can use to avoid redundant wiring
public static class RecipeCapabilities
{
    // True if a service type is already registered
    public static bool ServiceExists(this IServiceCollection services, Type serviceType)
        => services.Any(d => d.ServiceType == serviceType);

    public static bool ServiceExists<TService>(this IServiceCollection services)
        => services.Any(d => d.ServiceType == typeof(TService));

    // True if options TOptions has at least one configure action bound
    public static bool OptionsConfigured<TOptions>(this IServiceCollection services)
        where TOptions : class
    {
        var configure = typeof(Microsoft.Extensions.Options.IConfigureOptions<TOptions>);
        var post = typeof(Microsoft.Extensions.Options.IPostConfigureOptions<TOptions>);
        return services.Any(d => configure.IsAssignableFrom(d.ServiceType) || post.IsAssignableFrom(d.ServiceType));
    }
}
