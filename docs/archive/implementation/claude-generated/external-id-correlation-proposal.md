# External ID Correlation Framework Proposal

## Executive Summary

This proposal outlines the implementation of a framework-level external ID correlation system for Koan Flow entities to enable automatic cross-system entity tracking and correlation. The system will automatically populate `identifier.external.{source}:{id}` structures in canonical projections and create indexed key relationships for efficient parent-child resolution across different source systems.

## Problem Statement

### Current State

Currently, different systems (BMS, OEM, etc.) may have data for the same physical entity (e.g., a sensor) but with different native IDs, formats, and timing. While external identifier data exists in source entities, the framework lacks:

1. **Automatic External ID Population**: No framework-level processing of external identifiers from source data into canonical models
2. **Cross-System Correlation**: No automatic correlation of the same entity from different systems
3. **Parent-Child Resolution**: No indexed external ID lookups for parent relationship resolution
4. **Timeline Consolidation**: No merging of data from different systems under unified canonical entities

### Data Evidence

**Current Keyed Entity** (has entity data but missing external ID structure):

```json
{
  "_id": "112fabe4ce5743d6a6c5bf6d9fb11211",
  "SourceId": "S1",
  "Data": {
    "deviceId": "D1",
    "SerialNumber": "S1",
    "code": "TEMP",
    "unit": "C",
    "id": "S1"
  },
  "Source": {
    "system": "oem",
    "adapter": "oem"
  }
}
```

**Current Canonical Model** (missing external ID correlation):

```json
{
  "_id": "canonical::K4JV48YABMVHQ1CHC7KC2S0330",
  "Model": {
    "deviceId": ["D1"],
    "SerialNumber": ["S1"],
    "code": ["TEMP"],
    "unit": ["C"],
    "id": ["S1"]
    // ❌ Missing: identifier.external.oem: "S1"
  }
}
```

**Manufacturer Data** (shows external ID patterns in source data):

```json
{
  "identifier.external.bms": "BMS-MFG-001",
  "identifier.external.oem": "OEM-VENDOR-42"
}
```

## Proposed Solution

### 1. Framework-Level External ID Auto-Population

Enhance the canonical projection pipeline to automatically extract and populate external identifiers:

**Target Canonical Structure**:

```json
{
  "_id": "canonical::K4JV48YABMVHQ1CHC7KC2S0330",
  "Model": {
    "deviceId": ["K4KDERR71A6AQN9ZRGV5ADPGG0"], // ✅ Canonical Device ULID (resolved from source "D1")
    "SerialNumber": ["S1"],
    "code": ["TEMP"],
    "unit": ["C"],
    "identifier": {
      "external": {
        "oem": "S1", // ✅ Auto-populated from Source + entity ID
        "bms": "S1_BMS" // ✅ When same sensor arrives from BMS
      }
    }
  }
}
```

### 2. Policy-Driven Configuration

Enable fine-grained control per entity type with policy attributes:

```csharp
[FlowPolicy(ExternalIdPolicy = ExternalIdPolicy.AutoPopulate)]
public class Sensor : FlowEntity<Sensor>
{
    [AggregationKey]
    public string SerialNumber{ get; set; } = default!;
    // Framework automatically creates: identifier.external.{source}:{id}
}

[FlowPolicy(ExternalIdPolicy = ExternalIdPolicy.AutoPopulate,
           ExternalIdKey = "identifier.code")]
public class Manufacturer : DynamicFlowEntity<Manufacturer>
{
    // Framework uses identifier.code value for external ID generation
}

public enum ExternalIdPolicy
{
    AutoPopulate,     // Framework auto-generates external IDs (Default)
    Manual,           // Developer explicitly provides external IDs
    Disabled,         // No external ID tracking
    SourceOnly        // Only track source, not individual IDs
}
```

### 3. Enhanced IdentityLink Indexing

Leverage existing `IdentityLink<T>` infrastructure with automatic index creation:

```json
// IdentityLink entry (already exists)
{
  "Id": "oem|oem|S1",
  "System": "oem",
  "Adapter": "oem",
  "ExternalId": "S1",
  "ReferenceUlid": "K4JV48YABMVHQ1CHC7KC2S0330"
}
```

### 4. Cross-System Parent Resolution

Enable efficient parent lookups across systems with canonical ULID resolution:

```csharp
// Sensor entity from BMS system references deviceId: "bmsD1"
// Framework automatically resolves:
// 1. Look up external ID: "bms|bms|bmsD1" in IdentityLink → ReferenceUlid: "K4KDERR71A6AQN9ZRGV5ADPGG0"
// 2. Replace source parent ID with canonical ULID in canonical model
// 3. Result: deviceId: ["K4KDERR71A6AQN9ZRGV5ADPGG0"] in canonical projection
// 4. Preserve external ID correlation: identifier.external.bms: "bmsS1"
```

## Implementation Analysis

### Current Implementation State (Updated 2025-01-07)

**✅ FULLY IMPLEMENTED Components**:

1. **IdentityLink<T> Model**: Complete external ID → ReferenceUlid mapping system (`src/Koan.Flow.Core/Model/Identity.cs:7`)
2. **Canonical Projection Pipeline**: Enhanced with external ID auto-population (`src/Koan.Flow.Core/ServiceCollectionExtensions.cs:217-250`)
3. **FlowRegistry**: GetExternalIdKeys method with policy-driven detection (`src/Koan.Flow.Core/Infrastructure/FlowRegistry.cs:106-119`)
4. **Data/Source Separation**: Clean separation in StageRecord<T> with Data and Source properties (`src/Koan.Flow.Core/Model/Typed.cs:57-59`)
5. **External ID Resolution**: Working IdentityLink resolution in intake pipeline (`src/Koan.Flow.Core/ServiceCollectionExtensions.cs:636-709`)
6. **Reserved Key Infrastructure**: Constants for `identifier.external.*` prefix (`src/Koan.Flow.Core/Infrastructure/Constants.cs:66`)
7. **Policy Framework**: FlowPolicyAttribute with ExternalIdPolicy enum (`src/Koan.Flow.Core/Attributes/FlowPolicyAttribute.cs`)
8. **Source Entity ID Extraction**: GetSourceEntityId() extracts [Key] property values (`ServiceCollectionExtensions.cs:926-972`)
9. **ParentKey Resolution**: TryResolveParentViaExternalId() for cross-system parent lookup (`ServiceCollectionExtensions.cs:806-831`)
10. **Source ID Stripping**: Canonical models exclude source 'id' fields (`ServiceCollectionExtensions.cs:256-259`)
11. **IdentityLink Auto-Creation**: CreateOrUpdateIdentityLinks() indexes external IDs (`ServiceCollectionExtensions.cs:978-1041`)

**🔄 Implementation Status**:
The external ID correlation infrastructure is **FULLY FUNCTIONAL**. The system now:

- ✅ Automatically populates `identifier.external.{source}` with source entity IDs (from [Key] property, NOT aggregation keys)
- ✅ Strips source-specific 'id' fields from canonical and root models
- ✅ Resolves ParentKey relationships via external ID lookups to canonical ULIDs
- ✅ Parks entities with PARENT_NOT_FOUND when parents haven't arrived yet
- ✅ Creates and maintains IdentityLink indexes for efficient lookups

### Implementation Delta (COMPLETED)

#### Phase 1: Core External ID Processing ✅ (COMPLETED)

**File**: `src/Koan.Flow.Core/ServiceCollectionExtensions.cs`
**Location**: Lines 208-231 (canonical projection loop)
**Current State**: Processing only Data payload, no Source metadata integration
**Required Changes**:

```csharp
// CURRENT (lines 208-213)
foreach (var r in all)
{
    var src = (string)(r.GetType().GetProperty("SourceId")!.GetValue(r) ?? "unknown");
    var payload = r.GetType().GetProperty("Data")!.GetValue(r);
    var dict = ExtractDict(payload);
    // ... process only dict fields
}

// REQUIRED ENHANCEMENT
foreach (var r in all)
{
    var src = (string)(r.GetType().GetProperty("SourceId")!.GetValue(r) ?? "unknown");
    var payload = r.GetType().GetProperty("Data")!.GetValue(r);
    var sourceMetadata = r.GetType().GetProperty("Source")!.GetValue(r);
    var dict = ExtractDict(payload);
    var sourceDict = ExtractDict(sourceMetadata);

    // ✅ NEW: Auto-populate external ID from source system + primary entity ID
    if (sourceDict != null && dict != null) {
        var systemName = GetSourceSystem(sourceDict); // Extract from Source.system
        var primaryId = GetPrimaryEntityId(dict, modelType); // Get aggregation key value
        if (!string.IsNullOrEmpty(systemName) && !string.IsNullOrEmpty(primaryId)) {
            var externalIdKey = $"identifier.external.{systemName}";
            if (!canonical.ContainsKey(externalIdKey)) {
                canonical[externalIdKey] = new List<string?>();
            }
            canonical[externalIdKey].Add(primaryId);
        }
    }

    // Process regular fields...
}
```

**Supporting Methods Needed**:

- `GetSourceSystem(sourceDict)`: Extract system name from Source metadata
- `GetPrimaryEntityId(dict, modelType)`: Get aggregation key value for external ID

#### Phase 2: Policy Framework

**New File**: `src/Koan.Flow.Core/Attributes/FlowPolicyAttribute.cs`

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class FlowPolicyAttribute : Attribute
{
    public ExternalIdPolicy ExternalIdPolicy { get; set; } = ExternalIdPolicy.AutoPopulate;
    public string? ExternalIdKey { get; set; }
}

public enum ExternalIdPolicy
{
    AutoPopulate,
    Manual,
    Disabled,
    SourceOnly
}
```

#### Phase 3: Enhanced FlowRegistry

**File**: `src/Koan.Flow.Core/Infrastructure/FlowRegistry.cs`
**Method**: `GetExternalIdKeys` (lines 104-108)
**Current State**: Returns empty array with comment "vNext: rely on reserved identifier.external.\* keys"
**Required Changes**:

```csharp
// CURRENT (lines 104-108)
/// <summary>
/// External-id property discovery via attributes is deprecated. Reserved keys (identifier.external.*) are used.
/// </summary>
public static string[] GetExternalIdKeys(Type modelType)
{
    // vNext: rely on reserved identifier.external.* keys; explicit [EntityLink] is removed.
    return s_externalIdProps.GetOrAdd(modelType, static _ => Array.Empty<string>());
}

// AFTER (Policy-driven implementation)
public static string[] GetExternalIdKeys(Type modelType)
{
    return s_externalIdProps.GetOrAdd(modelType, type => {
        var policy = type.GetCustomAttribute<FlowPolicyAttribute>();
        if (policy?.ExternalIdPolicy == ExternalIdPolicy.AutoPopulate) {
            // Return the key field to use for external ID generation
            return new[] { policy.ExternalIdKey ?? GetDefaultAggregationKey(type) };
        }
        return Array.Empty<string>();
    });
}

private static string GetDefaultAggregationKey(Type modelType)
{
    var aggTags = GetAggregationTags(modelType);
    return aggTags.Length > 0 ? aggTags[0] : "id";
}
```

#### Phase 4: Auto-Index Management

**File**: `src/Koan.Flow.Core/ServiceCollectionExtensions.cs`
**Addition**: Automatic IdentityLink creation with proper indexing

```csharp
// After canonical projection, create/update IdentityLink entries
await CreateOrUpdateIdentityLinks(modelType, refUlid, externalIds, stoppingToken);
```

## Benefits Analysis

### Cross-System Entity Correlation

- **Before**: Sensor S1 from OEM and Sensor S1_BMS from BMS are separate entities
- **After**: Both correlate to the same canonical entity with `identifier.external.oem: "S1"` and `identifier.external.bms: "S1_BMS"`

### Efficient Parent Resolution

- **Before**: Parent lookups require expensive cross-system queries
- **After**: O(1) parent resolution via indexed external ID lookups

### Timeline Consolidation

- **Before**: OEM sends data at 5-minute intervals, BMS at 1-minute intervals - separate timelines
- **After**: Both data streams merge into single canonical entity timeline

### Zero-Config Developer Experience

- **Before**: Developers must manually implement cross-system correlation
- **After**: Framework automatically handles correlation with simple policy attribute

## Migration Strategy

### 1. Backward Compatibility

- Existing entities without external ID policies continue working unchanged
- New external ID fields are additive to canonical models
- No breaking changes to existing API contracts

### 2. Gradual Rollout

- **Phase 1**: Core external ID processing for new entities
- **Phase 2**: Policy framework for fine-grained control
- **Phase 3**: Enhanced indexing and performance optimization
- **Phase 4**: Migration tooling for existing entities

### 3. Testing Strategy

- Unit tests for external ID extraction logic
- Integration tests for cross-system correlation scenarios
- Performance tests for large-scale external ID lookups
- Migration validation for existing canonical models

## Success Metrics

### Functional Metrics

- ✅ External IDs automatically populated in canonical models
- ✅ Cross-system entity correlation working (same physical entity, multiple systems)
- ✅ Parent-child relationships resolved via external ID lookups
- ✅ Zero-configuration experience for standard use cases

### Performance Metrics

- ✅ O(1) external ID lookups via proper indexing
- ✅ No performance regression in canonical projection pipeline
- ✅ Efficient storage of external ID correlation data

### Developer Experience Metrics

- ✅ Policy-driven configuration reduces boilerplate code
- ✅ Clear documentation and examples for external ID correlation
- ✅ Seamless migration path from existing implementations

## Implementation Summary

### Effort Assessment

**Low Effort (1-2 days)**: The external ID correlation infrastructure is **90% complete**. The primary gap is automatic external ID population in the canonical projection pipeline.

**Core Implementation Requirements**:

1. **Phase 1** (Essential): Add 10-15 lines of code to canonical projection loop for Source metadata processing
2. **Phase 2** (Recommended): Create FlowPolicyAttribute class (~30 lines) for policy-driven configuration
3. **Phase 3** (Enhancement): Update FlowRegistry.GetExternalIdKeys to support policy detection (~20 lines)
4. **Phase 4** (Optional): Enhanced IdentityLink management automation

**Technical Debt**: The current implementation has infrastructure ready but lacks the final integration step between Source metadata and external ID generation.

### Development Priority

**Immediate (Phase 1)**: ⭐⭐⭐

- Add automatic `identifier.external.{system}` population from Source metadata during canonical projection
- Enables cross-system entity correlation with minimal code changes
- Leverages existing IdentityLink infrastructure completely

**Short-term (Phase 2-3)**: ⭐⭐

- FlowPolicy attribute system for fine-grained control
- Enhanced FlowRegistry for policy-driven external ID key detection

**Long-term (Phase 4)**: ⭐

- Performance optimizations and enhanced indexing management

## Conclusion

The External ID Correlation Framework provides a comprehensive solution for cross-system entity tracking and correlation within Koan Flow. The infrastructure is **already in place** - only the final integration between Source metadata and external ID generation is missing.

By leveraging existing infrastructure (IdentityLink, canonical projections, reserved key processing) and adding minimal automatic external ID processing, the framework will enable seamless correlation of entities across multiple source systems while maintaining excellent developer experience and performance characteristics.

**Key Finding**: The capability exists but is dormant - it requires manual external ID specification rather than automatic generation from source systems. The proposed solution activates existing infrastructure with minimal implementation effort.
