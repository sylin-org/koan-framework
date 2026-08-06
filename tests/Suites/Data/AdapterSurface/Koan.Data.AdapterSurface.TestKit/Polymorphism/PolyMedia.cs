using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>Shared DATA-0109 entity root used by every adapter-surface conformance suite.</summary>
public class PolyMedia : Entity<PolyMedia>
{
    public string Kind { get; set; } = "";
    public string FamilyTag { get; set; } = "";
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public PolyMedia? Related { get; set; }
}
