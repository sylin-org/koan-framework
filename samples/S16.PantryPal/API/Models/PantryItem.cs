using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.PantryPal.Models;

/// <summary>
/// Current pantry inventory with AI vision detection support.
/// Tracks quantities, expiration dates, and source metadata for learning.
/// </summary>
[McpEntity(Name = "PantryItem", Description = "Pantry inventory with AI vision support and expiration tracking")]
public sealed class PantryItem : Entity<PantryItem>
{
    // ==========================================
    // Core Data
    // ==========================================

    /// <summary>Normalized item name (e.g., "chicken breast", "black beans")</summary>
    public string Name { get; set; } = "";

    public decimal Quantity { get; set; }

    /// <summary>Unit of measurement (e.g., lbs, oz, whole, can)</summary>
    public string Unit { get; set; } = "";

    /// <summary>Item category (e.g., produce, dairy, meat, canned, pantry)</summary>
    public string Category { get; set; } = "uncategorized";

    /// <summary>Storage location: pantry, fridge, freezer</summary>
    public string Location { get; set; } = "pantry";

    /// <summary>Item status: available, expired, consumed, wasted</summary>
    public string Status { get; set; } = "available";

    public DateTime? ExpiresAt { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public decimal? PurchasePrice { get; set; }

    /// <summary>Auto-restock items like flour, oil, salt</summary>
    public bool IsStaple { get; set; }

    // ==========================================
    // AI Vision Metadata
    // ==========================================

    /// <summary>How this item was added: manual, photo, barcode</summary>
    public string Source { get; set; } = "manual";

    /// <summary>Reference to PantryPhoto if added via vision</summary>
    public string? SourcePhotoId { get; set; }

    /// <summary>Vision metadata if added via photo detection</summary>
    public VisionMetadata? VisionMetadata { get; set; }

    // ==========================================
    // Product Details (from vision/barcode)
    // ==========================================

    public string? Brand { get; set; }
    public string? Barcode { get; set; }
    public string? ProductImageUrl { get; set; }
}

/// <summary>
/// Vision processing metadata for items added via photo detection.
/// </summary>
public class VisionMetadata
{
    public string? SourcePhotoId { get; set; }
    public string? DetectionId { get; set; }
    public float Confidence { get; set; }
    public bool WasUserCorrected { get; set; }
}
