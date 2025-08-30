using Sora.Data.Core.Model;

namespace S7.ContentPlatform.Models;

/// <summary>
/// Represents a content category in the platform.
/// Categories help organize articles by topic.
/// </summary>
public sealed class Category : Entity<Category>
{
    /// <summary>
    /// The category display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Detailed description of the category.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// URL-friendly slug for the category.
    /// </summary>
    public string Slug { get; set; } = string.Empty;
    
    /// <summary>
    /// Hex color code for category theming.
    /// </summary>
    public string? ColorHex { get; set; }
    
    /// <summary>
    /// Icon name or URL for the category.
    /// </summary>
    public string? IconName { get; set; }
    
    /// <summary>
    /// Display order for category listing.
    /// </summary>
    public int SortOrder { get; set; }
    
    /// <summary>
    /// Whether the category is active and visible.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When the category was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
