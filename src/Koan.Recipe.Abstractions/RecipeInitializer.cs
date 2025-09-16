using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;

namespace Koan.Recipe.Abstractions;

internal sealed class RecipeInitializer : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Read assembly-level attributes for self-registered recipes
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var attrs = asm.GetCustomAttributes(typeof(KoanRecipeAttribute), false).Cast<KoanRecipeAttribute>();
                foreach (var a in attrs) RecipeRegistry.Register(a.RecipeType);
            }
            catch { /* ignore */ }
        }

        // Defer application until a ServiceProvider exists: we need IConfiguration/IHostEnvironment
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanInitializer>(sp => new RecipeApplier()));
    }
}