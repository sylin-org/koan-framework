namespace Sora.Web.GraphQl.Types;

public sealed class EntityView
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Display { get; set; }
}

public sealed class EntityCollection
{
    public List<EntityView> Items { get; set; } = new();
    public int TotalCount { get; set; }
}
