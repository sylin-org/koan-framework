using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;

namespace Koan.Recipe.Abstractions;

public static class KoanRecipeServiceCollectionExtensions
{
    private const string ConfigRoot = "Koan:Recipes";

    public static IServiceCollection AddRecipe<T>(this IServiceCollection services)
        where T : class, IKoanRecipe
    {
        RecipeRegistry.Register<T>();
        return services;
    }

    public static IServiceCollection AddRecipe(this IServiceCollection services, string name)
    {
        services.PostConfigure<KoanRecipeOptions>(o =>
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
        services.AddOptions<KoanRecipeOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanInitializer>(sp => new RecipeInitializer()));
    }
}