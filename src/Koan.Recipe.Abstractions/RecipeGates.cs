using Microsoft.Extensions.Configuration;

namespace Koan.Recipe.Abstractions;

public static class RecipeGates
{
    public static bool ForcedOverridesEnabled(IConfiguration cfg, string recipeName)
    {
        var global = cfg.GetValue<bool?>("Koan:Recipes:AllowOverrides") ?? false;
        if (!global) return false;
        var per = cfg.GetValue<bool?>($"Koan:Recipes:{recipeName}:ForceOverrides") ?? false;
        return per;
    }
}