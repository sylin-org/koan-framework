using Sora.Data.Core.Model;

namespace S7.TechDocs.Models;

public class UserRating : Entity<UserRating>
{
    // Composite-style Id: "{userId}:{documentId}"
    public string DocumentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}