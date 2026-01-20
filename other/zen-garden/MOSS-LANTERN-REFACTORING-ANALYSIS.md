# Moss & Lantern Refactoring Analysis

**Date:** January 19, 2026  
**Context:** Deep code scan for magic strings, duplicated code, and shared concerns across Moss, Lantern, and Rake

---

## Executive Summary

### Shared Concerns Across All Three Components

**Environment Variables:**
- `STONE_NAME` (Moss)
- `STONE_HOST` (Moss)  
- `LANTERN_ENDPOINT` (Moss)
- `GARDEN_STONE` (Rake) ✅ Already using constant
- `NO_COLOR` (Rake) ✅ Already using constant
- `GARDEN_UNICODE` (Rake) ✅ Already using constant

**API Endpoints:**
- `/health` - Used by Moss, Lantern, and queried by Rake
- `/capabilities` - Moss endpoint, queried by Rake
- `/api/v1/*` - Moss API prefix (20+ routes)
- `/api/*` - Lantern API prefix (5 routes)

**HTTP Headers:**
- `Authorization` (Lantern auth.rs)
- `Bearer` token prefix (Lantern)

**Status/State Strings:**
- Health statuses: "healthy", "degraded", "unhealthy" ✅ Already centralized
- Service statuses: "running", "stopped", etc. ✅ Already centralized
- Job statuses: "completed", "success", "failed", "error" ✅ Already centralized

---

## Moss-Specific Issues

### 1. ✅ ALREADY FIXED - Environment Variable Names
**Status:** Already using centralized constants after earlier refactoring

- `STONE_NAME` - Line 2197
- `STONE_HOST` - Line 2314
- `LANTERN_ENDPOINT` - Line 2346

**Action Required:** Add these to centralized constants

### 2. ⚠️ API Path Duplication
**Location:** src/moss/src/main.rs lines 2522-2562

**Pattern:** 40+ route definitions with hardcoded paths:
```rust
.route("/api/v1/offerings", get(api::v1::offerings::list_offerings_v1))
.route("/api/v1/offerings", post(api::v1::offerings::plant_offering_v1))
.route("/api/v1/offerings/:name", get(api::v1::offerings::get_offering_v1))
// ... 37 more similar routes
```

**Issues:**
- Magic string `/api/v1/` prefix repeated 40+ times
- Route parameter patterns like `:name`, `:service` not centralized
- Difficult to change API version or path structure

**Recommendation:**
- **Low Priority** - This is Axum routing DSL, changing it adds complexity
- Consider API path constants only if versioning changes are planned
- Current approach is idiomatic Rust/Axum code

### 3. ⚠️ HTTP Header String (Low Priority)
**Location:** src/lantern/src/auth.rs line 22

```rust
.get("authorization")
```

**Issue:** Hardcoded header name

**Recommendation:**
- Add HTTP header constants: `HEADER_AUTHORIZATION`, `HEADER_BEARER_PREFIX`
- Shared with Rake if auth headers are used there

---

## Lantern-Specific Issues

### 1. ⚠️ API Path Duplication  
**Location:** src/lantern/src/main.rs lines 112-117

```rust
.route("/health", get(handlers::health))
.route("/api/register", axum::routing::post(handlers::register))
.route("/api/resolve", get(handlers::resolve))
.route("/api/stones", get(handlers::list_stones))
.route("/api/topology", get(handlers::get_topology))
.route("/api/events/stream", get(handlers::event_stream))
```

**Issues:**
- Magic string `/api/` prefix repeated
- `/health` endpoint path not centralized
- Test file (integration_tests.rs) duplicates same paths

**Recommendation:**
- **Low Priority** - Same as Moss, idiomatic Axum routing
- Consider if API redesign is planned

### 2. ⚠️ HTTP Header and Auth Strings
**Location:** src/lantern/src/auth.rs lines 22-29

```rust
.get("authorization")  // Line 22
"Missing authorization header"  // Line 24
"Invalid authorization format"  // Line 29
```

**Issues:**
- Hardcoded header name
- Hardcoded error messages (could be constants)

**Recommendation:**
- Add auth-related constants
- Consider moving to garden_common if Moss will use auth

---

## Shared Constants to Add

### 1. **Environment Variables** (High Priority)

```rust
// In src/common/src/constants/mod.rs

// Stone/Node identification
pub const ENV_STONE_NAME: &str = "STONE_NAME";
pub const ENV_STONE_HOST: &str = "STONE_HOST";

// Service discovery
pub const ENV_LANTERN_ENDPOINT: &str = "LANTERN_ENDPOINT";

// Already exist (from Rake refactoring):
// pub const ENV_GARDEN_STONE: &str = "GARDEN_STONE";
// pub const ENV_NO_COLOR: &str = "NO_COLOR";
// pub const ENV_GARDEN_UNICODE: &str = "GARDEN_UNICODE";
```

**Impact:** 3 locations in Moss

### 2. **API Endpoints** (Medium Priority)

```rust
// Common endpoint paths
pub const ENDPOINT_HEALTH: &str = "/health";
pub const ENDPOINT_CAPABILITIES: &str = "/capabilities";

// API path prefixes (if needed for client code)
pub const API_V1_PREFIX: &str = "/api/v1";
pub const API_PREFIX: &str = "/api";
```

**Impact:** 
- Useful for Rake's client code (20+ locations construct URLs)
- Less useful for Moss/Lantern routing (DSL style)
- **Primary Value:** Rake URL construction

### 3. **HTTP Headers** (Medium Priority)

```rust
// HTTP headers
pub const HEADER_AUTHORIZATION: &str = "authorization";
pub const HEADER_CONTENT_TYPE: &str = "content-type";
pub const HEADER_ACCEPT: &str = "accept";

// Auth schemes
pub const AUTH_BEARER_PREFIX: &str = "Bearer ";
```

**Impact:** 1 location in Lantern, potentially more if auth added to Moss

### 4. **Default Values** (Low Priority)

```rust
// Default stone name fallback
pub const DEFAULT_STONE_NAME: &str = "stone-01";

// Already exists from earlier refactoring:
// pub const VALUE_UNKNOWN: &str = "unknown";
```

**Impact:** 1 location in Moss (line 2220)

---

## Recommendations

### Phase 1: High-Value, Low-Risk (Immediate)

1. **Add Environment Variable Constants** ✅ **RECOMMENDED**
   - Add `ENV_STONE_NAME`, `ENV_STONE_HOST`, `ENV_LANTERN_ENDPOINT`
   - Update Moss main.rs (3 locations)
   - Export via garden_common lib.rs
   - **Effort:** 15 minutes
   - **Impact:** Type safety, consistency with Rake patterns

2. **Add HTTP Header Constants** ✅ **RECOMMENDED**
   - Add `HEADER_AUTHORIZATION`, `AUTH_BEARER_PREFIX`
   - Update Lantern auth.rs (2 locations)
   - **Effort:** 10 minutes
   - **Impact:** Consistency, prep for future auth in Moss

3. **Add Default Value Constants** ✅ **RECOMMENDED**
   - Add `DEFAULT_STONE_NAME`
   - Update Moss main.rs (1 location)
   - **Effort:** 5 minutes
   - **Impact:** Eliminates magic string, consistent with VALUE_UNKNOWN

### Phase 2: API Endpoint Constants (Optional)

4. **Add Common Endpoint Path Constants** ⚠️ **CONSIDER**
   - Add `ENDPOINT_HEALTH`, `ENDPOINT_CAPABILITIES`, `API_V1_PREFIX`
   - **Primary Use Case:** Rake URL construction (high value)
   - **Secondary:** Moss/Lantern routing (low value - idiomatic DSL)
   - Update Rake main.rs (20+ URL constructions)
   - **Effort:** 30 minutes
   - **Impact:** Reduces Rake URL construction errors, easier to refactor API paths

5. **Route Path Constants for Axum** ❌ **NOT RECOMMENDED**
   - Creating constants for Axum `.route()` calls adds complexity
   - Current DSL approach is Rust/Axum best practice
   - Only consider if planning major API redesign

---

## Code Duplication Analysis

### No Significant Duplication Found

**Moss:**
- No duplicated functions identified
- Compatibility check logic is properly abstracted
- Route definitions are declarative (not duplication)

**Lantern:**
- Election logic is standalone (no duplication)
- Registry operations are properly abstracted
- Test files have test-specific setup (expected)

**Rake:**
- ✅ Service table rendering duplication **already fixed**

---

## Implementation Priority

### Tier 1: Do Now (30 minutes total)
- ✅ Environment variable constants (ENV_STONE_NAME, ENV_STONE_HOST, ENV_LANTERN_ENDPOINT)
- ✅ HTTP header constants (HEADER_AUTHORIZATION, AUTH_BEARER_PREFIX)
- ✅ Default value constant (DEFAULT_STONE_NAME)

### Tier 2: Consider Later (30 minutes)
- ⚠️ API endpoint constants for Rake URL construction
  - **Value:** High for Rake, low for Moss/Lantern
  - **Risk:** Low - only affects client code

### Tier 3: Skip
- ❌ Axum route path constants
  - **Reason:** Diminishing returns, fights framework idioms

---

## Summary of Changes

### Constants to Add (src/common/src/constants/mod.rs)

```rust
// ============================================================================
// Environment Variables (Moss-specific)
// ============================================================================

/// Stone name identifier (STONE_NAME environment variable)
pub const ENV_STONE_NAME: &str = "STONE_NAME";

/// Stone host address (STONE_HOST environment variable)
pub const ENV_STONE_HOST: &str = "STONE_HOST";

/// Lantern service discovery endpoint (LANTERN_ENDPOINT environment variable)
pub const ENV_LANTERN_ENDPOINT: &str = "LANTERN_ENDPOINT";

// ============================================================================
// HTTP Headers
// ============================================================================

/// HTTP Authorization header name
pub const HEADER_AUTHORIZATION: &str = "authorization";

/// Bearer token authentication scheme prefix
pub const AUTH_BEARER_PREFIX: &str = "Bearer ";

// ============================================================================
// Default Values
// ============================================================================

/// Default stone name when no configuration is provided
pub const DEFAULT_STONE_NAME: &str = "stone-01";

// ============================================================================
// API Endpoint Paths (Optional - for client code)
// ============================================================================

/// Health check endpoint path
pub const ENDPOINT_HEALTH: &str = "/health";

/// Hardware capabilities endpoint path
pub const ENDPOINT_CAPABILITIES: &str = "/capabilities";

/// Moss API v1 prefix
pub const API_V1_PREFIX: &str = "/api/v1";

/// Lantern API prefix
pub const API_PREFIX: &str = "/api";
```

### Files to Update

**Moss (src/moss/src/main.rs):**
- Line 2197: `std::env::var("STONE_NAME")` → `std::env::var(garden_common::ENV_STONE_NAME)`
- Line 2220: `"stone-01"` → `garden_common::DEFAULT_STONE_NAME`
- Line 2314: `std::env::var("STONE_HOST")` → `std::env::var(garden_common::ENV_STONE_HOST)`
- Line 2346: `std::env::var("LANTERN_ENDPOINT")` → `std::env::var(garden_common::ENV_LANTERN_ENDPOINT)`

**Lantern (src/lantern/src/auth.rs):**
- Line 22: `.get("authorization")` → `.get(garden_common::HEADER_AUTHORIZATION)`
- Line 29: `"Bearer "` → `garden_common::AUTH_BEARER_PREFIX`

**Rake (src/rake/src/main.rs) - Optional Tier 2:**
- 20+ locations: `/health`, `/capabilities`, `/api/v1/` URL construction

**Common (src/common/src/lib.rs):**
- Add exports for new constants

---

## Validation Tests

After implementing changes:

1. **Moss:**
   - Verify stone name resolution with STONE_NAME env var
   - Verify Lantern endpoint discovery
   - Verify health check endpoint responds

2. **Lantern:**
   - Verify auth header parsing (if tests exist)
   - Verify health endpoint responds

3. **Rake:**
   - Run existing test suite (24 tests should pass)
   - Manually test `list` command with stone endpoint

---

## Conclusion

**High-Value Changes:**
- Environment variable constants (Moss): Type safety + consistency
- HTTP header constants (Lantern): Prep for future auth expansion
- Default value constants: Eliminates last magic strings

**Moderate-Value Changes:**
- API endpoint constants for Rake URL construction

**Low-Value Changes:**
- Axum route path constants (fights framework idioms)

**Estimated Total Effort:** 30-60 minutes depending on scope

**Risk Level:** Low - All changes are additive constants with simple replacements
