namespace Koan.Web.Connector.GraphQl.Types;

public sealed class EntityCollection
{
    public List<EntityView> Items { get; set; } = new();
    public long TotalCount { get; set; }
}
