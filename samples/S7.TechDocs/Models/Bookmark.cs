using Koan.Data.Core.Model;

namespace S7.TechDocs.Models;

public class Bookmark : Entity<Bookmark>
{
    // Use a composite-style Id to ensure per-user per-document uniqueness: "{userId}:{documentId}"
    public string DocumentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}