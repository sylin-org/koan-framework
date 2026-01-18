# API Error Response Standardization

## Summary

Standardized error responses across the moss daemon API using a consistent error envelope structure. All error-prone endpoints now return predictable, machine-readable error codes with detailed context.

## Implementation

### 1. Error Envelope Structure (zen-common)

Added to [zen-common/src/lib.rs](other/zen-garden/src/linux/common/src/lib.rs):

```rust
/// Standardized error response envelope for all API endpoints
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApiError {
    pub error: ErrorDetails,
}

/// Error details with code, message, and optional context
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ErrorDetails {
    pub code: String,
    pub message: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub details: Option<HashMap<String, serde_json::Value>>,
}
```

### 2. Error Codes Implemented

| Error Code | Description | Used By |
|------------|-------------|---------|
| `offering_not_found` | Requested offering/service does not exist | POST /api/operations/offer, POST /api/operations/remove, POST /api/operations/offer (batch) |
| `docker_error` | Docker operation failed (pull, start, remove) | POST /api/operations/remove |
| `invalid_component` | Unknown component type for refresh operation | POST /api/system/refresh |
| `insufficient_resources` | Disk/permission/IO errors during operations | POST /api/system/refresh |

### 3. Updated Endpoints

#### POST /api/operations/offer/{offering}

**Before:**
```json
{
  "status": "error",
  "message": "Unknown offering: postgresql",
  "error": "Template not found: postgresql.yaml"
}
```

**After:**
```json
{
  "error": {
    "code": "offering_not_found",
    "message": "Unknown offering: postgresql",
    "details": {
      "offering": "postgresql",
      "template_error": "Template not found: postgresql.yaml"
    }
  }
}
```

#### POST /api/operations/remove/{offering_id}

**Before (not found):**
```json
{
  "error": "not_found",
  "message": "Service 'mongodb' not found"
}
```

**After (not found):**
```json
{
  "error": {
    "code": "offering_not_found",
    "message": "Service 'mongodb' not found",
    "details": {
      "offering_id": "mongodb"
    }
  }
}
```

**Before (docker error):**
```json
{
  "error": "remove_failed",
  "message": "Failed to remove: Container removal failed"
}
```

**After (docker error):**
```json
{
  "error": {
    "code": "docker_error",
    "message": "Failed to remove service: Container removal failed",
    "details": {
      "offering_id": "mongodb",
      "docker_error": "Container removal failed"
    }
  }
}
```

#### POST /api/system/refresh

**Before (invalid component):**
```json
{
  "status": "error",
  "message": "Unknown component: invalid-component"
}
```

**After (invalid component):**
```json
{
  "error": {
    "code": "invalid_component",
    "message": "Unknown component: invalid-component",
    "details": {
      "component": "invalid-component",
      "valid_components": ["moss", "garden-rake"]
    }
  }
}
```

**Before (IO error):**
```json
{
  "status": "error",
  "message": "Failed to create staging directory",
  "error": "Permission denied"
}
```

**After (IO error):**
```json
{
  "error": {
    "code": "insufficient_resources",
    "message": "Failed to create staging directory",
    "details": {
      "directory": "/home/stone/bin",
      "io_error": "Permission denied (os error 13)"
    }
  }
}
```

#### POST /api/operations/offer (batch)

**Before:**
```json
{
  "status": "error",
  "message": "One or more unknown offerings",
  "invalid_offerings": ["redis", "postgresql"]
}
```

**After:**
```json
{
  "error": {
    "code": "offering_not_found",
    "message": "One or more unknown offerings",
    "details": {
      "invalid_offerings": ["redis", "postgresql"],
      "valid_count": 1
    }
  }
}
```

## Benefits

1. **Consistency**: All error responses follow the same structure
2. **Machine-readable**: Error codes enable programmatic error handling
3. **Context-rich**: Details object provides debugging information
4. **Forward-compatible**: New details can be added without breaking clients
5. **Type-safe**: Strongly typed in Rust with serde serialization

## Error Code Usage Guidelines

- **offering_not_found**: Template/service doesn't exist in catalog
- **docker_error**: Docker daemon interaction failed
- **invalid_component**: Unknown refresh component (not moss/garden-rake)
- **insufficient_resources**: File system, permissions, or resource errors

## Future Error Codes (Not Yet Implemented)

- `port_conflict`: Port already in use by another container
- `invalid_template`: Template file is malformed or has validation errors
- `compatibility_failed`: Hardware doesn't meet offering requirements
- `network_error`: Network connectivity issues
- `auth_required`: Authentication/authorization failure (Pond phase)

## Testing Recommendations

```bash
# Test offering_not_found
curl -X POST http://localhost:3001/api/operations/offer/nonexistent

# Test invalid_component
curl -X POST http://localhost:3001/api/system/refresh \
  -H "Content-Type: application/json" \
  -d '{"component":"invalid","architecture":"x86_64-unknown-linux-gnu","binary":"..."}'

# Test batch offering_not_found
curl -X POST http://localhost:3001/api/operations/offer \
  -H "Content-Type: application/json" \
  -d '{"offerings":["mongodb","invalid1","invalid2"]}'
```

## Implementation Notes

- Error responses maintain HTTP status codes (404, 400, 500) for semantic meaning
- Success responses remain unchanged (no breaking changes to happy path)
- The `details` field is optional and omitted when empty
- Error codes use snake_case for consistency with Rust conventions
- All error responses are logged with appropriate tracing levels
