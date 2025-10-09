using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.PantryPal.Models;

/// <summary>
/// Recipe with nutritional data, instructions, and AI-powered metadata.
/// Supports semantic search via embeddings and smart ingredient matching.
/// </summary>
[McpEntity(Name = "Recipe", Description = "Recipes with nutritional data and cooking instructions")]
public sealed class Recipe : Entity<Recipe>
{
    // ==========================================
    // Basic Information
    // ==========================================

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Cuisine types (e.g., Italian, Mexican, Thai, American)</summary>
    public string[] Cuisines { get; set; } = Array.Empty<string>();

    /// <summary>When to serve (e.g., breakfast, lunch, dinner, snack)</summary>
    public string[] MealTypes { get; set; } = Array.Empty<string>();

    /// <summary>Dietary tags (e.g., vegetarian, vegan, gluten-free, dairy-free)</summary>
    public string[] DietaryTags { get; set; } = Array.Empty<string>();

    // ==========================================
    // Effort & Time
    // ==========================================

    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public int TotalTimeMinutes { get; set; }

    /// <summary>Difficulty level: easy, medium, hard</summary>
    public string Difficulty { get; set; } = "medium";

    public int Servings { get; set; } = 4;

    // ==========================================
    // Nutrition (per serving)
    // ==========================================

    public int Calories { get; set; }
    public int ProteinGrams { get; set; }
    public int CarbsGrams { get; set; }
    public int FatGrams { get; set; }
    public int FiberGrams { get; set; }

    // ==========================================
    // Ingredients (structured for smart matching)
    // ==========================================

    public RecipeIngredient[] Ingredients { get; set; } = Array.Empty<RecipeIngredient>();

    // ==========================================
    // Instructions
    // ==========================================

    public string[] Steps { get; set; } = Array.Empty<string>();

    // ==========================================
    // Metadata
    // ==========================================

    public string? ImageUrl { get; set; }
    public string? SourceUrl { get; set; }

    /// <summary>Estimated cost per serving in USD</summary>
    public decimal EstimatedCost { get; set; }

    /// <summary>Good for making multiple portions at once</summary>
    public bool IsBatchFriendly { get; set; }

    /// <summary>Can be frozen for later</summary>
    public bool IsFreezerFriendly { get; set; }

    /// <summary>Required equipment (e.g., oven, blender, slow cooker)</summary>
    public string[] RequiredEquipment { get; set; } = Array.Empty<string>();

    // ==========================================
    // AI Enhancement
    // ==========================================

    /// <summary>
    /// Vector embedding for semantic search.
    /// Enables queries like "something creamy and comforting" to match appropriate recipes.
    /// </summary>
    public float[]? Embedding { get; set; }

    // ==========================================
    // User Feedback
    // ==========================================

    /// <summary>Average rating from user feedback (1-5 stars)</summary>
    public float AverageRating { get; set; }

    /// <summary>Number of times this recipe has been cooked by users</summary>
    public int TimesCooked { get; set; }
}

/// <summary>
/// Individual ingredient in a recipe with parsing support.
/// </summary>
public class RecipeIngredient
{
    /// <summary>Normalized ingredient name (e.g., "chicken breast", "black beans")</summary>
    public string Name { get; set; } = "";

    public decimal Amount { get; set; }

    /// <summary>Unit of measurement (e.g., cup, tbsp, gram, whole)</summary>
    public string Unit { get; set; } = "";

    /// <summary>Preparation notes (e.g., "diced", "minced", "optional")</summary>
    public string? Notes { get; set; }

    public bool IsOptional { get; set; }
}
