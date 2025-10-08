using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.PantryPal.Models;

/// <summary>
/// Planned or cooked meals with user feedback for AI learning.
/// Tracks nutrition consumption and user ratings.
/// </summary>
[McpEntity(Name = "MealPlan", Description = "Planned and cooked meals with user feedback and ratings")]
public sealed class MealPlan : Entity<MealPlan>
{
    // ==========================================
    // Recipe Reference
    // ==========================================

    public string RecipeId { get; set; } = "";

    /// <summary>Denormalized for quick access without joins</summary>
    public string RecipeName { get; set; } = "";

    // ==========================================
    // Scheduling
    // ==========================================

    public DateTime ScheduledFor { get; set; }

    /// <summary>Meal type: breakfast, lunch, dinner, snack</summary>
    public string MealType { get; set; } = "dinner";

    public int Servings { get; set; }

    // ==========================================
    // Status Tracking
    // ==========================================

    /// <summary>Status: planned, prepped, cooked, skipped</summary>
    public string Status { get; set; } = "planned";

    public DateTime? CookedAt { get; set; }

    // ==========================================
    // User Feedback (AI Learning)
    // ==========================================

    /// <summary>User rating (1-5 stars)</summary>
    public int? Rating { get; set; }

    public string? Notes { get; set; }

    /// <summary>User tags (e.g., "too spicy", "kid-approved", "make again")</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    // ==========================================
    // Actual Consumption
    // ==========================================

    /// <summary>Actual servings consumed (may differ from planned)</summary>
    public int ActualServings { get; set; }
}
