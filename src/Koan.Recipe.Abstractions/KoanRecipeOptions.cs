namespace Koan.Recipe.Abstractions;

public sealed class KoanRecipeOptions
{
    public string[] Active { get; set; } = Array.Empty<string>();
    public bool AllowOverrides { get; set; } = false;
    public bool DryRun { get; set; } = false;
    // Per-recipe flags live under Koan:Recipes:<RecipeName>:ForceOverrides
}