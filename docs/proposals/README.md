# Koan Framework Proposals

This directory contains architectural proposals for the Instance-Based Relationship System.

## Active Proposals

### 1. [Universal Instance-Based Relationship System](parent-relationship-system.md)
**Status**: RFC - Ready for Implementation
**Objective**: Replace Flow-specific `ParentKeyAttribute` with universal instance-based semantic methods

**Key Features**:
- **Semantic Methods**: `model.GetParent()`, `model.GetChildren()` with cardinality validation
- **Clean Streaming**: `Data<Order, string>.AllStream().Relatives()` syntax for batch operations
- **Selective Enrichment**: Only requested entities get `RelationshipGraph` format
- **Direct Migration**: Remove `ParentKeyAttribute` entirely - no compatibility bridge

**Impact**: All Koan modules gain intuitive relationship navigation with performance optimization

### 2. [Implementation Roadmap](implementation-roadmap.md)
**Status**: Work Breakdown Structure
**Timeline**: 4 weeks, 3 phases

**Phase Overview**:
- **Phase 1** (Weeks 1-2): Entity Enhancement with Semantic Methods
- **Phase 2** (Week 3): Direct Flow Migration (Breaking Change)
- **Phase 3** (Week 4): Web API Response Format Update

### 3. [Relationship Response Format v2](relationship-response-format-v2.md)
**Status**: Specification
**Objective**: Selective enrichment with child class grouping and raw relationship data

**Enhanced Format**:
```json
{
  "entity": { /* enriched entity properties */ },
  "parents": {
    "CustomerId": { /* raw customer object */ },
    "CategoryId": { /* raw category object */ }
  },
  "children": {
    "OrderItem": {
      "OrderId": [ /* raw order item objects */ ]
    },
    "Review": {
      "OrderId": [ /* raw review objects */ ]
    }
  }
}
```

## Implementation Priority

1. **Start Here**: [Universal Instance-Based Relationship System](parent-relationship-system.md) - Complete specification
2. **Follow**: [Implementation Roadmap](implementation-roadmap.md) - 4-week work breakdown
3. **Reference**: [Relationship Response Format v2](relationship-response-format-v2.md) - API response structure

## Key Architectural Changes

### Breaking Changes (By Design)
- **No Compatibility Bridge**: Direct replacement of `ParentKeyAttribute` with `ParentAttribute`
- **Response Format**: Replace `_parent` format with `RelationshipGraph<T>` structure
- **API Parameters**: Use `?with=all` instead of `?with=relatives`

### New Capabilities
- **Semantic Validation**: `model.GetParent()` validates single-parent constraint
- **Instance Methods**: All relationship navigation through entity instance methods
- **Streaming Syntax**: `Data<T>.AllStream().Relatives()` for high-performance batch processing
- **Selective Enrichment**: Only requested entities receive enriched format

## Usage Examples

### Semantic Single Parent/Child
```csharp
// ✅ Works for single-parent models
var orderItem = await Data<OrderItem, string>.GetAsync("item-123");
var order = await orderItem.GetParent<Order>();      // Typed
var parent = await orderItem.GetParent();           // Object

// ❌ Throws for multi-parent models with clear error message
var order = await Data<Order, string>.GetAsync("order-456");
var parent = await order.GetParent();
// InvalidOperationException: "Order has multiple parents. Use GetParents() instead"
```

### Clean Streaming Syntax
```csharp
// ✅ High-performance streaming with relationship enrichment
await foreach (var enriched in Data<Order, string>.AllStream().Relatives())
{
    Console.WriteLine($"Order: {enriched.Entity.Id}");
    Console.WriteLine($"Customer: {enriched.Parents["CustomerId"]}");
}
```

## Next Steps

1. **Review**: [Universal Instance-Based Relationship System](parent-relationship-system.md) specification
2. **Implement**: Begin Phase 1 following [Implementation Roadmap](implementation-roadmap.md)
3. **Migrate**: Update existing Flow code to use new instance methods

**Note**: This approach prioritizes clean architecture over backward compatibility, delivering a superior developer experience through semantic validation and intuitive instance-based API design.