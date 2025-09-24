using Koan.Data.Core.Model;

namespace S10.DevPortal.Models;

/// <summary>
/// Demonstrates self-referencing hierarchy + soft relationships with relationship navigation
/// </summary>
public class Technology : Entity<Technology>
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ParentId { get; set; }  // Hierarchical relationships
    public List<string> RelatedIds { get; set; } = new();  // Soft relationships demo
    public string? OfficialUrl { get; set; }
}