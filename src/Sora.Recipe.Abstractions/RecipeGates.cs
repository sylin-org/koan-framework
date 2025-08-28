using Microsoft.Extensions.Configuration;

namespace Sora.Recipe.Abstractions;

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