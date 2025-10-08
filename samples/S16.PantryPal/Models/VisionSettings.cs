using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.PantryPal.Models;

/// <summary>
/// User preferences for AI vision features and learning configuration.
/// Tracks user corrections to improve detection accuracy.
/// </summary>
[McpEntity(Name = "VisionSettings", Description = "User vision preferences and AI learning configuration")]
public sealed class VisionSettings : Entity<VisionSettings>
{
    public string UserId { get; set; } = "";

    // ==========================================
    // Auto-Confirmation Thresholds
    // ==========================================

    /// <summary>Auto-add items above this confidence threshold (0.0-1.0)</summary>
    public float AutoConfirmThreshold { get; set; } = 0.95f;

    /// <summary>Enable auto-confirmation (disabled by default for safety)</summary>
    public bool AutoConfirmEnabled { get; set; } = false;

    // ==========================================
    // Default Preferences
    // ==========================================

    /// <summary>Default storage location for new items</summary>
    public string DefaultLocation { get; set; } = "pantry";

    /// <summary>Default expiration days if none detected</summary>
    public int DefaultExpirationDays { get; set; } = 7;

    // ==========================================
    // Learning from Corrections
    // ==========================================

    /// <summary>Track user corrections to improve AI</summary>
    public bool EnableLearning { get; set; } = true;

    /// <summary>User's preferred names (AI name → User's preferred name)</summary>
    public Dictionary<string, string> UserCorrections { get; set; } = new();

    // ==========================================
    // Privacy
    // ==========================================

    /// <summary>Keep original photos after processing</summary>
    public bool StoreOriginalPhotos { get; set; } = true;

    /// <summary>Auto-delete photos older than this many days</summary>
    public int PhotoRetentionDays { get; set; } = 30;
}
