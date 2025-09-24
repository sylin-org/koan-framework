using Koan.Data.Core.Model;

namespace S10.DevPortal.Models;

/// <summary>
/// Threaded comment system demonstrating relationship navigation
/// </summary>
public class Comment : Entity<Comment>
{
    public string ArticleId { get; set; } = "";  // Parent article
    public string UserId { get; set; } = "";     // Comment author
    public string? ParentCommentId { get; set; } = "";  // Threading support
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}