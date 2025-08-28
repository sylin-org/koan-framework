using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Recipe.Abstractions;

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