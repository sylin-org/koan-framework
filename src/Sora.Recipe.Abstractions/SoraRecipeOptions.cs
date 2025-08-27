namespace Sora.Recipe.Abstractions;

public sealed class SoraRecipeOptions
{
    public string[] Active { get; set; } = Array.Empty<string>();
    public bool AllowOverrides { get; set; } = false;
    public bool DryRun { get; set; } = false;
    // Per-recipe flags live under Sora:Recipes:<RecipeName>:ForceOverrides
}