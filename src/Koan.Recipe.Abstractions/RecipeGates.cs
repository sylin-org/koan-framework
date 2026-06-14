using Microsoft.Extensions.Configuration;

namespace Koan.Recipe.Abstractions;

public static class RecipeGates
{
    public static bool ForcedOverridesEnabled(IConfiguration cfg, string recipeName)
    {
        var global = cfg.GetValue<bool?>(Infrastructure.ConfigurationConstants.Keys.AllowOverrides) ?? false;
        if (!global) return false;
        var per = cfg.GetValue<bool?>(Infrastructure.ConfigurationConstants.ForceOverrides(recipeName)) ?? false;
        return per;
    }
}