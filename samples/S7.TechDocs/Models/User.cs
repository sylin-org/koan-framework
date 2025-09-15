using Koan.Data.Core.Model;

namespace S7.TechDocs.Models;

public class User : Entity<User>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    public int DocumentsCreated { get; set; }
    public int DocumentsReviewed { get; set; }
}