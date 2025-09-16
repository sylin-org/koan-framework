# Flow Key Resolution Issue - Root Cause Analysis & Fix

## Problem Statement

Flow entities with `[AggregationKey]` attributes are being parked with `ReasonCode: "NO_KEYS"` despite having the required key values in the payload.

## Root Cause Analysis

### Observed Behavior
```json
// Device model with AggregationKey
public sealed class Device : FlowEntity<Device>
{
    [AggregationKey]
    public string Serial { get; set; } = default!;
}

// Parked record shows case mismatch
{
  "ReasonCode": "NO_KEYS",
  "StagePayload": {
    "serial": "SN-MRI-7000-002",  // ← lowercase in JSON
    // ...
  },
  "Evidence": {
    "reason": "no-values",
    "tags": ["Serial"]              // ← capitalized in lookup
  }
}
```

### Root Cause: JSON Serialization Case Inconsistency
1. **C# Property**: `public string Serial { get; set; }` (PascalCase)
2. **JSON Serialization**: Converts to `"serial"` (camelCase) 
3. **Key Extraction Logic**: Looks for `"Serial"` (PascalCase)
4. **Result**: Case mismatch prevents key resolution

### Why This Happened
- Transport envelope creation uses JSON serialization
- Default .NET JSON serialization converts PascalCase → camelCase
- Aggregation key extraction expects original property case
- No consistent casing policy across the Flow pipeline

## Decision: Implement System-Wide JSON Casing Configuration

### Rationale
1. **Root Cause Fix**: Addresses underlying serialization inconsistency
2. **System-Wide Solution**: Fixes issue for all Flow entities
3. **Framework-Level**: Maintains clean developer experience  
4. **Predictable**: Ensures consistent JSON casing throughout pipeline

### Alternative Approaches Considered
- ❌ Case-insensitive key lookup (bandaid, doesn't fix root cause)
- ❌ JsonPropertyName attributes (requires per-property changes)
- ❌ Transport envelope fixes (too narrow, doesn't address pipeline-wide issue)

## Implementation Plan

1. **Configure JSON Serialization**: Set consistent camelCase policy
2. **Enable Case-Insensitive Deserialization**: Handle existing mixed-case data
3. **Update Flow Pipeline Components**: Ensure consistent JSON handling
4. **Test Key Resolution**: Verify aggregation keys resolve correctly

## Implementation Details

### Fix Applied
Modified `FlowRegistry.GetAggregationTags()` to convert C# property names to JSON property names:

```csharp
// Before: tags.Add(p.Name);  // "Serial"
// After: 
var jsonPropertyName = GetJsonPropertyName(p);  // "serial"
tags.Add(jsonPropertyName);

private static string GetJsonPropertyName(PropertyInfo property)
{
    // Check for explicit JsonProperty attribute
    var jsonPropertyAttr = property.GetCustomAttribute<JsonPropertyAttribute>();
    if (jsonPropertyAttr?.PropertyName != null)
    {
        return jsonPropertyAttr.PropertyName;
    }
    
    // Use camelCase conversion to match CamelCasePropertyNamesContractResolver
    var camelCaseResolver = new CamelCasePropertyNamesContractResolver();
    return camelCaseResolver.GetResolvedPropertyName(property.Name);
}
```

### Behavior Change
- **Before**: `GetAggregationTags()` returns `["Serial"]` (C# property name)  
- **After**: `GetAggregationTags()` returns `["serial"]` (JSON property name)
- **Result**: Key extraction matches payload: `"serial": "SN-MRI-7000-002"`

## Expected Outcome

After fix:
- Aggregation key extraction uses JSON property names (`"serial"` not `"Serial"`)
- Device entities with `[AggregationKey]` resolve keys successfully  
- Flow entities progress through intake → standardized → keyed stages
- No more `NO_KEYS` parking due to case mismatches
- Consistent behavior across all Flow entity types