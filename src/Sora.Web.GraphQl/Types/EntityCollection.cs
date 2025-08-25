namespace Sora.Web.GraphQl.Types;

public sealed class EntityCollection
{
    public List<EntityView> Items { get; set; } = new();
    public int TotalCount { get; set; }
}