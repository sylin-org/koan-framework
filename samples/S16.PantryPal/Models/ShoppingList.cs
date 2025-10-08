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
    /// <summary>Week or period this list is for</summary>
    public DateTime CreatedFor { get; set; }

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

    /// <summary>Store section for organization (e.g., produce, meat, dairy, pantry)</summary>
    public string StoreSection { get; set; } = "";

    public bool IsPurchased { get; set; }
    public decimal EstimatedPrice { get; set; }

    /// <summary>Which recipes need this ingredient</summary>
    public string[] NeededForRecipes { get; set; } = Array.Empty<string>();
}
