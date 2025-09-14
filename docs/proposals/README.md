# Sora Framework Proposals

This directory contains architectural proposals for enhancing the Sora Framework.

## Active Proposals

### 1. [Parent Relationship System](parent-relationship-system.md)
**Status**: RFC - Ready for Implementation
**Objective**: Migrate parent relationship functionality from Flow-specific to universal cross-module system

**Key Changes**:
- Move from `[ParentKey]` to simplified `[Parent]` attribute
- Enable parent relationships across all Sora modules (not just Flow)
- Add GraphQL-like REST API support with `?with=` parameter
- Performance optimization via provider-specific capabilities

**Impact**: All Sora modules can define parent-child relationships with enhanced REST API querying

### 2. [Implementation Roadmap](implementation-roadmap.md)
**Status**: Work Breakdown Structure
**Timeline**: 8 weeks, 4 phases

**Phase Overview**:
- **Phase 1** (Weeks 1-2): Data Layer Foundation
- **Phase 2** (Weeks 3-4): Provider Enhancements
- **Phase 3** (Weeks 5-6): Web API Integration
- **Phase 4** (Weeks 7-8): Flow Migration

### 3. [Relationship Response Format v2](relationship-response-format-v2.md)
**Status**: Specification
**Objective**: Define clean API response structure for parent/child relationships

**Format**:
```json
{
  "entity": { /* model properties */ },
  "parents": {
    "CustomerId": { /* customer object */ },
    "CategoryId": { /* category object */ }
  },
  "children": {
    "OrderItem": [ /* order item objects */ ] // Name of child class.
  }
}
```

## Implementation Priority

1. **Start Here**: [Parent Relationship System](parent-relationship-system.md) - Complete specification
2. **Follow**: [Implementation Roadmap](implementation-roadmap.md) - Detailed work breakdown
3. **Reference**: [Relationship Response Format v2](relationship-response-format-v2.md) - API response structure

## Agent-Friendly Structure

These documents are optimized for both human and AI agent consumption:

- **Clear Sections**: Each document has well-defined sections with consistent structure
- **Code Examples**: Concrete implementation examples with before/after comparisons
- **Actionable Tasks**: Implementation roadmap includes specific commands and validation steps
- **Decision Context**: Key architectural decisions explained with rationale

## Next Steps

1. Review and approve [Parent Relationship System](parent-relationship-system.md) specification
2. Begin Phase 1 implementation following [Implementation Roadmap](implementation-roadmap.md)
3. Use [Relationship Response Format v2](relationship-response-format-v2.md) for API design

All proposals maintain backward compatibility and provide clear migration paths for existing code.