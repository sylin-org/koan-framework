using Koan.Data.Core.Model;

namespace S10.DevPortal.Models;

/// <summary>
/// Demonstrates Entity&lt;T&gt; with auto GUID v7 generation and relationship navigation
/// </summary>
public class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public ResourceType Type { get; set; } = ResourceType.Article;
    public string? TechnologyId { get; set; }  // Parent relationship demo
    public string AuthorId { get; set; } = "";  // User relationship demo
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsPublished { get; set; } = false;
}

public enum ResourceType
{
    Article,
    Tutorial  // Simplified for demo focus
}