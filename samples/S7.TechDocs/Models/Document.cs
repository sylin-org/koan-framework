using Koan.Data.Core.Model;

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