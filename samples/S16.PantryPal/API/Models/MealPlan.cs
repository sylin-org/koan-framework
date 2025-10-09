using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.PantryPal.Models;

/// <summary>
/// Multi-day meal plan with scheduled meals and nutrition tracking.
/// </summary>
[McpEntity(Name = "MealPlan", Description = "Multi-day meal plans with scheduled meals")]
public sealed class MealPlan : Entity<MealPlan>
{
    public string? UserId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>Planned meals within the date range</summary>
    public PlannedMeal[] PlannedMeals { get; set; } = Array.Empty<PlannedMeal>();

    public decimal EstimatedCost { get; set; }
    public string Status { get; set; } = "active";
}

/// <summary>
/// Individual planned or cooked meal with user feedback.
/// </summary>
public class PlannedMeal
{
    public string RecipeId { get; set; } = "";

    /// <summary>Denormalized for quick access</summary>
    public string RecipeName { get; set; } = "";

    public DateTime ScheduledFor { get; set; }

    /// <summary>Meal type: breakfast, lunch, dinner, snack</summary>
    public string MealType { get; set; } = "dinner";

    public int Servings { get; set; }

    /// <summary>Status: planned, prepped, cooked, skipped</summary>
    public string Status { get; set; } = "planned";

    public DateTime? CookedAt { get; set; }

    // User Feedback
    public int? Rating { get; set; }
    public string? Notes { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();

    public int ActualServings { get; set; }
}
