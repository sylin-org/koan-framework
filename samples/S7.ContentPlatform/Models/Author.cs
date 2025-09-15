using Koan.Data.Core.Model;

namespace S7.ContentPlatform.Models;

/// <summary>
/// Represents an author in the content platform.
/// Authors can write articles and manage their profile.
/// </summary>
public sealed class Author : Entity<Author>
{
    /// <summary>
    /// The author's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The author's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Author's biographical information.
    /// </summary>
    public string Bio { get; set; } = string.Empty;
    
    /// <summary>
    /// URL to the author's profile picture.
    /// </summary>
    public string? AvatarUrl { get; set; }
    
    /// <summary>
    /// Author's website or portfolio URL.
    /// </summary>
    public string? WebsiteUrl { get; set; }
    
    /// <summary>
    /// Social media links.
    /// </summary>
    public Dictionary<string, string> SocialLinks { get; set; } = new();
    
    /// <summary>
    /// When the author joined the platform.
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether the author is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Author's role/permissions level.
    /// </summary>
    public AuthorRole Role { get; set; } = AuthorRole.Writer;
}