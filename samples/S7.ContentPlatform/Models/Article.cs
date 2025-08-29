using Sora.Data.Core.Model;

namespace S7.ContentPlatform.Models;

/// <summary>
/// Represents an article in the content platform.
/// Demonstrates moderation workflow (draft → review → published) and soft-delete capabilities.
/// </summary>
public sealed class Article : Entity<Article>
{
    /// <summary>
    /// The article title.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Brief summary or excerpt of the article.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// Full article content (Markdown format).
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// ID of the author who wrote this article.
    /// </summary>
    public string AuthorId { get; set; } = string.Empty;
    
    /// <summary>
    /// ID of the category this article belongs to.
    /// </summary>
    public string CategoryId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current status of the article in the publication workflow.
    /// </summary>
    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;
    
    /// <summary>
    /// When the article was published (if published).
    /// </summary>
    public DateTime? PublishedAt { get; set; }
    
    /// <summary>
    /// When the article was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Tags associated with the article.
    /// </summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>
    /// SEO-friendly URL slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;
    
    /// <summary>
    /// Estimated reading time in minutes.
    /// </summary>
    public int ReadingTimeMinutes { get; set; }
    
    /// <summary>
    /// Editor feedback or rejection reason (used in moderation workflow).
    /// </summary>
    public string? EditorFeedback { get; set; }
}

/// <summary>
/// Article status in the publication workflow.
/// </summary>
public enum ArticleStatus
{
    /// <summary>
    /// Article is being written, not ready for review.
    /// </summary>
    Draft = 0,
    
    /// <summary>
    /// Article submitted for editorial review.
    /// </summary>
    UnderReview = 1,
    
    /// <summary>
    /// Article approved and published.
    /// </summary>
    Published = 2,
    
    /// <summary>
    /// Article rejected by editor, needs revision.
    /// </summary>
    Rejected = 3,
    
    /// <summary>
    /// Published article that has been archived/unpublished.
    /// </summary>
    Archived = 4
}
