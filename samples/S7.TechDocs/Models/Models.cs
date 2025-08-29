using Sora.Data.Core.Model;

namespace S7.TechDocs.Models;

public class Document : Entity<Document>
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = Infrastructure.Constants.DocumentStatus.Draft;
    public string CollectionId { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public int ViewCount { get; set; }
    public double Rating { get; set; }
    public int RatingCount { get; set; }
    public string? ReviewerId { get; set; }
    public string? ReviewNotes { get; set; }
}

public class Collection : Entity<Collection>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "📄";
    public int DocumentCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDefault { get; set; }
}

public class User : Entity<User>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    public int DocumentsCreated { get; set; }
    public int DocumentsReviewed { get; set; }
}

public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public double Rating { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Snippet { get; set; } = string.Empty;
    public double Relevance { get; set; }
}

public class AIAssistance
{
    public List<string> SuggestedTags { get; set; } = new();
    public string GeneratedToc { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public List<string> QualityIssues { get; set; } = new();
    public List<string> RelatedDocuments { get; set; } = new();
    public List<string> ImprovementSuggestions { get; set; } = new();
}

public class Bookmark : Entity<Bookmark>
{
    // Use a composite-style Id to ensure per-user per-document uniqueness: "{userId}:{documentId}"
    public string DocumentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserRating : Entity<UserRating>
{
    // Composite-style Id: "{userId}:{documentId}"
    public string DocumentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class IssueReport : Entity<IssueReport>
{
    public string DocumentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
