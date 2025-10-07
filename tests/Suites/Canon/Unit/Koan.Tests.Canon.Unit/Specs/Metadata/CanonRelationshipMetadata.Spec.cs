using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Core.Relationships;

namespace Koan.Tests.Canon.Unit.Specs.Metadata;

public sealed class CanonRelationshipMetadataSpec
{
    private readonly ITestOutputHelper _output;

    public CanonRelationshipMetadataSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Parent_relationships_are_discovered()
        => TestPipeline.For<CanonRelationshipMetadataSpec>(_output, nameof(Parent_relationships_are_discovered))
            .Act(ctx =>
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

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Child_relationships_scan_cached_assemblies()
        => TestPipeline.For<CanonRelationshipMetadataSpec>(_output, nameof(Child_relationships_scan_cached_assemblies))
            .Act(ctx =>
            {
                var cache = AssemblyCache.Instance;
                cache.AddAssembly(typeof(CanonRelationshipMetadataSpec).Assembly);

                var service = new RelationshipMetadataService();
                var children = service.GetChildRelationships(typeof(Order));

                children.Should().Contain(tuple => tuple.ReferenceProperty == nameof(OrderLine.OrderId) && tuple.ChildType == typeof(OrderLine));

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Canon]
    private sealed class Order : CanonEntity<Order>
    {
        [AggregationKey]
        public string OrderNumber { get; set; } = string.Empty;
    }

    private sealed class OrderLine : CanonEntity<OrderLine>
    {
        [AggregationKey]
        public string LineNumber { get; set; } = string.Empty;

        [Parent(typeof(Order))]
        public string OrderId { get; set; } = string.Empty;
    }

    private sealed class OrderAddress : CanonValueObject<OrderAddress>
    {
        [Parent(typeof(Order))]
        public string OrderId { get; set; } = string.Empty;
    }
}
