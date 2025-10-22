using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace S6.SnapVault.Models;

/// <summary>
/// Analysis style configuration for AI photo analysis
/// Stores PARAMETERS for prompt customization, not full prompts (factory pattern)
/// System styles are seeded on startup, users can create custom styles
/// </summary>
public class AnalysisStyle : Entity<AnalysisStyle>
{
    // ==================== Metadata ====================

    /// <summary>
    /// Display name (e.g., "Portrait & People")
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Emoji icon for UI dropdown (e.g., "👤")
    /// </summary>
    public string Icon { get; set; } = "🔍";

    /// <summary>
    /// User-friendly description shown in dropdown
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Display order in dropdown (lower = higher priority)
    /// </summary>
    public int Priority { get; set; } = 99;

    // ==================== Factory Parameters (Safe Customization) ====================

    /// <summary>
    /// Optional instructions prepended to base prompt
    /// Example: "Pay special attention to facial expressions and clothing details"
    /// </summary>
    public string? FocusInstructions { get; set; }

    /// <summary>
    /// Fact fields that are MANDATORY for this style (promoted from optional to required in JSON structure)
    /// These fields will appear as uncommented required fields in the JSON template
    /// Example: Portrait style makes ["subject 1"] mandatory
    /// Base mandatory facts (type, style, composition, etc.) are always included - this ADDS to that list
    /// </summary>
    public List<string> MandatoryFields { get; set; } = new();

    /// <summary>
    /// Optional fact fields that should be emphasized/encouraged for this style
    /// These remain commented examples but may have enhanced example values
    /// Example: Portrait emphasizes ["subject 2", "subject 3"] (conditional on visibility)
    /// </summary>
    public List<string> EmphasisFields { get; set; } = new();

    /// <summary>
    /// Optional facts that should be de-emphasized or omitted unless highly relevant
    /// Example: Product style de-emphasizes ["atmospherics", "locale cues"]
    /// </summary>
    public List<string> DeemphasizedFields { get; set; } = new();

    // ==================== Smart Classification ====================

    /// <summary>
    /// True if this is the "smart" style that uses two-stage classification
    /// </summary>
    public bool IsSmartStyle { get; set; } = false;

    /// <summary>
    /// Keywords used by classification prompt to detect this style
    /// Example: "people, faces, portraits, human subjects, emotion"
    /// </summary>
    public string? ClassificationKeywords { get; set; }

    // ==================== Advanced Customization (Escape Hatch) ====================

    /// <summary>
    /// Optional full prompt override for power users
    /// If set, factory uses this verbatim (bypasses all customization logic)
    /// WARNING: Must maintain same JSON structure as base prompt
    /// </summary>
    public string? FullPromptOverride { get; set; }

    // ==================== System Management ====================

    /// <summary>
    /// True if this is a system-seeded style (can't be deleted, only disabled)
    /// </summary>
    public bool IsSystemStyle { get; set; } = false;

    /// <summary>
    /// True if style was created by user (vs system-seeded)
    /// </summary>
    public bool IsUserCreated { get; set; } = false;

    /// <summary>
    /// Soft delete: inactive styles hidden from dropdown
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Template version for tracking upgrades to system styles
    /// Increment when base prompt or default styles change
    /// </summary>
    public int TemplateVersion { get; set; } = 1;

    // ==================== Audit ====================

    /// <summary>
    /// When style was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When style was last modified
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Who created this style (future: user ID)
    /// </summary>
    public string? CreatedBy { get; set; }
}
