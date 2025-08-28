using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Recipe.Abstractions;

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