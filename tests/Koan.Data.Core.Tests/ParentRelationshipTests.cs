using System;
using System.Linq;
using FluentAssertions;
using Koan.Data.Core.Relationships;
using Xunit;

namespace Koan.Data.Core.Tests;

public class ParentRelationshipTests
{
    public class ParentEntity { public string Id { get; set; } = "parent-1"; }
    public class ChildEntity {
        public string Id { get; set; } = "child-1";
        [Parent(typeof(ParentEntity))]
        public string ParentId { get; set; } = "parent-1";
    }

    [Fact]
    public void RelationshipMetadataService_FindsParentAttribute()
    {
        var svc = new RelationshipMetadataService();
        var rels = svc.GetParentRelationships(typeof(ChildEntity));
        rels.Should().ContainSingle();
        rels[0].PropertyName.Should().Be("ParentId");
        rels[0].ParentType.Should().Be(typeof(ParentEntity));
    }

    [Fact]
    public void CanGetParentIdValue()
    {
        var child = new ChildEntity();
        var svc = new RelationshipMetadataService();
        var rels = svc.GetParentRelationships(typeof(ChildEntity));
        var parentProp = typeof(ChildEntity).GetProperty(rels[0].PropertyName);
        var parentId = parentProp.GetValue(child);
        parentId.Should().Be("parent-1");
    }
}
