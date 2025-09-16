namespace Koan.Recipe.Abstractions;

// Contract for a recipe bundle

// Assembly-level registration for AOT-safe discovery (no broad scans)
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class KoanRecipeAttribute : Attribute
{
    public Type RecipeType { get; }
    public KoanRecipeAttribute(Type recipeType)
    {
        RecipeType = recipeType;
    }
}

// Minimal hosting environment fallback

// Structured event IDs for recipe bootstrap logs

// Optional capability gating helpers recipes can use to avoid redundant wiring