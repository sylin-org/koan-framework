using Koan.Data.Core.Model;

namespace Koan.Web.Sort.Tests;

/// <summary>
/// Test entity exercising scalar fields, nested objects, and a collection (the original-bug shape).
/// </summary>
public sealed class Widget : Entity<Widget>
{
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Sighting> Sightings { get; set; } = new();
}

public sealed class Sighting
{
    public string Location { get; set; } = "";
    public DateTimeOffset LastChangedAt { get; set; }
    public int Index { get; set; }
}
