using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Core.Relationships;

namespace Koan.Tests.Canon.Unit.Specs.Metadata;

public sealed class CanonRelationshipMetadataSpec
{
    [Fact]
    public void Parent_relationships_are_discovered()
    {
        var service = new RelationshipMetadataService();

        var parents = service.GetParentRelationships(typeof(OrderLine));

        parents.Should().ContainSingle();
        var parent = parents[0];
        parent.PropertyName.Should().Be(nameof(OrderLine.OrderId));
        parent.ParentType.Should().Be(typeof(Order));

        var valueObjectParents = service.GetParentRelationships(typeof(OrderAddress));
        valueObjectParents.Should().ContainSingle();
        valueObjectParents[0].ParentType.Should().Be(typeof(Order));
    }

    [Fact]
    public void Child_relationships_scan_cached_assemblies()
    {
        var cache = AssemblyCache.Instance;
        cache.AddAssembly(typeof(CanonRelationshipMetadataSpec).Assembly);

        var service = new RelationshipMetadataService();
        var children = service.GetChildRelationships(typeof(Order));

        children.Should().Contain(tuple => tuple.ReferenceProperty == nameof(OrderLine.OrderId) && tuple.ChildType == typeof(OrderLine));
    }

    [Canon]
    private sealed class Order : CanonEntity<Order>
    {
        [AggregationKey]
        public string OrderNumber { get; set; } = "";
    }

    private sealed class OrderLine : CanonEntity<OrderLine>
    {
        [AggregationKey]
        public string LineNumber { get; set; } = "";

        [Parent(typeof(Order))]
        public string OrderId { get; set; } = "";
    }

    private sealed class OrderAddress : CanonValueObject<OrderAddress>
    {
        [Parent(typeof(Order))]
        public string OrderId { get; set; } = "";
    }
}
