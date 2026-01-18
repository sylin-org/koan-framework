# CLI v1 API Migration

**Status:** Completed  
**Date:** 2026-01-17  
**Component:** garden-rake CLI

---

## Summary

Updated garden-rake CLI to use the new dual-layer v1 API endpoints. All zen commands now target the Offerings API (human-friendly layer), while technical commands use the Services API.

---

## Endpoint Migrations

### Offerings API (Zen Commands)

| Command | Old Endpoint | New Endpoint |
|---------|-------------|--------------|
| `explore` | `GET /api/offerings` | `GET /api/v1/offerings` |
| `offer <name>` | `GET /api/offerings/{name}` | `GET /api/v1/offerings/{name}` |
| `observe` | `GET /api/services` | `GET /api/v1/services` |
| `refresh` | `POST /api/offerings/refresh` | `POST /api/v1/offerings:refresh` |

### Services API (Technical Commands)

| Command | Old Endpoint | New Endpoint |
|---------|-------------|--------------|
| `templates` | `GET /api/templates` | `GET /api/v1/services/manifests` |
| `template <name>` | `GET /api/templates/{name}` | `GET /api/v1/services/{name}/manifest` |

### Unchanged Endpoints

| Endpoint | Reason |
|----------|--------|
| `GET /health` | Universal health check (root level) |
| `GET /capabilities` | Universal capabilities (root level) |
| `GET /api/stones` | Lantern topology API (different service) |

---

## Response Format Changes

### list_templates (manifests)

**Old Response:**
```json
{
  "templates": [...]
}
```

**New Response:**
```json
{
  "manifests": [...]
}
```

**CLI Change:** Updated JSON key from `templates` to `manifests` when parsing response.

---

## Custom Actions Format

Refresh command now uses single-colon custom action format:

**Old:** `POST /api/offerings/refresh`  
**New:** `POST /api/v1/offerings:refresh`

This aligns with industry standards (Kubernetes, GCP) and semantic clarity.

---

## Build Verification

Both components compile successfully:

```powershell
# CLI build
cd src/rake
cargo build
# ✓ Success

# API server build
cd src/moss
cargo build
# ✓ Success (33 warnings - cosmetic unused imports)
```

---

## Testing Notes

### Manual Verification Checklist

- [ ] `garden-rake explore` - Lists available offerings
- [ ] `garden-rake offer mongodb` - Shows offering details
- [ ] `garden-rake observe` - Lists installed services
- [ ] `garden-rake refresh` - Rebuilds offerings index
- [ ] `garden-rake templates` - Lists service manifests
- [ ] `garden-rake template mongodb` - Shows manifest YAML

### Expected Behavior

1. **Backwards compatibility:** Old moss versions (pre-v1) will return 404, CLI shows upgrade message
2. **Response parsing:** CLI correctly handles new response formats (manifests vs templates)
3. **Custom actions:** Refresh uses `:refresh` format successfully

---

## Next Steps

1. Update API-REFERENCE.md with v1 structure and examples
2. Add CLI integration tests for v1 endpoints
3. Document migration path for third-party tools
4. Consider adding `--api-version` flag for compatibility testing

---

## References

- **Design Document:** [API-V1-DUAL-LAYER-DESIGN.md](API-V1-DUAL-LAYER-DESIGN.md)
- **Architecture Decision:** [API-ARCHITECTURE-V1-EVALUATION.md](API-ARCHITECTURE-V1-EVALUATION.md)
- **Moss Implementation:** `src/moss/src/api/v1/`
- **CLI Implementation:** `src/rake/src/main.rs`
