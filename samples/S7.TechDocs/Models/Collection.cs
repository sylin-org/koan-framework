using Sora.Data.Core.Model;

namespace S7.TechDocs.Models;

public class Collection : Entity<Collection>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "📄";
    public int DocumentCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDefault { get; set; }
}