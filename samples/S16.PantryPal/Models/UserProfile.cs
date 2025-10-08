using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.PantryPal.Models;

/// <summary>
/// User preferences, dietary restrictions, and nutritional goals.
/// Enables personalized meal suggestions and learning.
/// </summary>
[McpEntity(Name = "UserProfile", Description = "User preferences, dietary goals, and cooking constraints")]
public sealed class UserProfile : Entity<UserProfile>
{
    // ==========================================
    // Personal Info
    // ==========================================

    public string Name { get; set; } = "";
    public int HouseholdSize { get; set; } = 1;

    // ==========================================
    // Dietary Preferences
    // ==========================================

    /// <summary>Dietary restrictions (e.g., vegetarian, vegan, gluten-free)</summary>
    public string[] DietaryRestrictions { get; set; } = Array.Empty<string>();

    public string[] Allergies { get; set; } = Array.Empty<string>();
    public string[] DislikedIngredients { get; set; } = Array.Empty<string>();
    public string[] FavoriteIngredients { get; set; } = Array.Empty<string>();
    public string[] FavoriteCuisines { get; set; } = Array.Empty<string>();

    // ==========================================
    // Nutrition Goals (daily targets)
    // ==========================================

    public int? TargetCalories { get; set; }
    public int? TargetProtein { get; set; }
    public int? TargetCarbs { get; set; }
    public int? TargetFat { get; set; }

    // ==========================================
    // Cooking Constraints
    // ==========================================

    /// <summary>Max cooking time on weekdays (minutes)</summary>
    public int MaxCookingTimeWeekday { get; set; } = 45;

    /// <summary>Max cooking time on weekends (minutes)</summary>
    public int MaxCookingTimeWeekend { get; set; } = 90;

    public string[] AvailableEquipment { get; set; } = Array.Empty<string>();

    /// <summary>Cooking experience: beginner, intermediate, advanced</summary>
    public string ExperienceLevel { get; set; } = "intermediate";

    // ==========================================
    // Budget & Shopping
    // ==========================================

    public decimal? WeeklyFoodBudget { get; set; }

    /// <summary>Plan meals for this many days ahead</summary>
    public int PreferredShoppingDays { get; set; } = 7;

    /// <summary>Prefer making large batches for meal prep</summary>
    public bool PreferBatchCooking { get; set; }

    // ==========================================
    // Learning Data
    // ==========================================

    /// <summary>Cuisine preferences learned from ratings</summary>
    public Dictionary<string, float> CuisineRatings { get; set; } = new();

    public int TotalMealsCooked { get; set; }
}
