using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sora.Recipe.Abstractions;

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