using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.PantryPal.Models;

/// <summary>
/// Consolidated shopping list with store organization and budget tracking.
/// Auto-generated from meal plans and pantry gaps.
/// </summary>
[McpEntity(Name = "ShoppingList", Description = "Shopping lists with store organization and budget tracking")]
public sealed class ShoppingList : Entity<ShoppingList>
{
    public string Name { get; set; } = "";

    /// <summary>Reference to meal plan this list was generated from</summary>
    public string? MealPlanId { get; set; }

    /// <summary>Week or period this list is for</summary>
    public DateTime CreatedFor { get; set; } = DateTime.UtcNow;

    /// <summary>Status: active, purchased, archived</summary>
    public string Status { get; set; } = "active";

    public ShoppingItem[] Items { get; set; } = Array.Empty<ShoppingItem>();

    public decimal EstimatedTotal { get; set; }
    public DateTime? PurchasedAt { get; set; }
}

/// <summary>
/// Individual shopping list item with store organization.
/// </summary>
public class ShoppingItem
{
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";

    /// <summary>Item category for organization (e.g., produce, meat, dairy, pantry)</summary>
    public string Category { get; set; } = "";

    public bool IsPurchased { get; set; }
    public decimal EstimatedPrice { get; set; }

    /// <summary>Which recipes need this ingredient</summary>
    public string[] NeededForRecipes { get; set; } = Array.Empty<string>();
}
