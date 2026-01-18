# Zen Garden Quick Reference

## Zen Syntax Cheat Sheet

### Discovery & Observation
```bash
garden-rake explore              # List all offerings
garden-rake garden               # View all stones
garden-rake observe              # Observe tended stone
garden-rake observe all          # Observe all stones
garden-rake touch                # Deep stone inspection
```

### Service Management
```bash
garden-rake offer mongodb        # Install MongoDB
garden-rake rest redis           # Stop Redis
garden-rake wake redis           # Start Redis
garden-rake nourish mongodb      # Upgrade MongoDB
garden-rake release redis        # Remove Redis
```

### Targeting & Modifiers
```bash
# Target specific stone
garden-rake touch at stone-02
garden-rake offer mongodb at http://192.168.1.100:7185

# Quiet mode (no suggestions)
garden-rake observe quietly
garden-rake garden quietly

# Combined
garden-rake offer redis at stone-03 quietly
```

### Watching & Streaming
```bash
garden-rake watch                # Stream events
garden-rake watch until 'ready'  # Exit when condition met
```

## Normative Syntax Reference

### Same Operations, Normative Style
```bash
garden-rake offer                # explore → offer
garden-rake observe              # garden → observe
garden-rake observe --quiet      # quietly → --quiet
garden-rake status --at stone-02 # touch at → status --at
```

## v1 API Endpoints

### Services
```bash
GET    /api/v1/services                 # List all services
GET    /api/v1/services/:service        # Get service details
POST   /api/v1/services                 # Create service (stub)
DELETE /api/v1/services/:service        # Delete service (stub)
POST   /api/v1/services/:service/rest   # Stop service (stub)
POST   /api/v1/services/:service/wake   # Start service (stub)
POST   /api/v1/services/:service/nourish # Upgrade service (stub)
```

### Garden & Stone
```bash
GET /api/v1/garden                      # All stones overview
GET /api/v1/garden/stones/:name         # Specific stone
GET /api/v1/stone                       # Local stone info
```

### Pond Security (Phase 3 Stubs)
```bash
POST   /api/v1/pond/init                # Initialize pond
DELETE /api/v1/pond                     # Remove pond
POST   /api/v1/pond/invite              # Invite stone
POST   /api/v1/pond/join                # Join pond
DELETE /api/v1/pond/stones/:name        # Untrust stone
GET    /api/v1/pond/status              # Pond status
```

## Quiet Mode

### Three Ways to Suppress Suggestions

1. **Zen keyword**: `garden-rake observe quietly`
2. **CLI flag**: `garden-rake observe --quiet` or `-q`
3. **Environment**: `$env:GARDEN_QUIET=1; garden-rake observe`

All three add `X-Quiet: true` header to API requests.

### API Usage
```bash
# With suggestions
curl http://localhost:7185/api/v1/stone

# Without suggestions  
curl -H "X-Quiet: true" http://localhost:7185/api/v1/stone
```

## Zen Verb → Normative Mapping

| Zen | Normative | Description |
|-----|-----------|-------------|
| `explore` | `offer` | List offerings |
| `garden` | `observe` | All stones |
| `observe` | `observe` | Stone snapshot |
| `touch` | `status` | Deep inspection |
| `offer` | `offer` | Install service |
| `rest` | `rest` | Stop service |
| `wake` | `wake` | Start service |
| `nourish` | `upgrade` | Upgrade service |
| `release` | `remove` | Delete service |
| `watch` | `watch` | Stream events |
| `tend` | `tend` | Manage tending |

## Examples

### Basic Workflow
```bash
# 1. Explore what's available
garden-rake explore

# 2. View your garden
garden-rake garden

# 3. Install a service
garden-rake offer mongodb

# 4. Check stone status
garden-rake touch

# 5. Watch events (quiet)
garden-rake watch quietly
```

### Multi-Stone Workflow
```bash
# 1. Observe all stones
garden-rake garden

# 2. Target specific stone
garden-rake touch at stone-02

# 3. Install on specific stone
garden-rake offer redis at stone-02

# 4. Observe that stone
garden-rake observe at stone-02
```

### Testing Workflow
```bash
# Start Moss
.\target\debug\garden-moss.exe --stone-name my-stone

# In another terminal, test zen syntax
garden-rake explore
garden-rake garden
garden-rake observe quietly

# Test v1 API
curl http://localhost:7185/api/v1/stone
curl -H "X-Quiet: true" http://localhost:7185/api/v1/garden
```

## Troubleshooting

### Stone Not Found
```bash
# Error: Could not resolve stone name 'stone-02'
# Solution: Use explicit URL or discover first
garden-rake observe                    # Populates cache
garden-rake touch at http://stone-02:7185  # Explicit URL
```

### Quiet Mode Not Working
```bash
# Check if X-Quiet header is sent
curl -v -H "X-Quiet: true" http://localhost:7185/api/v1/stone
# Look for: > X-Quiet: true

# Verify no suggestions in response
# Response should not have "suggestions" field
```

### Parser Errors
```bash
# Error: Cannot mix normative with zen keywords
# Bad:  garden-rake services list quietly
# Good: garden-rake explore quietly
# Good: garden-rake offer --quiet
```

## Useful Environment Variables

```bash
# Suppress suggestions globally
$env:GARDEN_QUIET = "1"

# Target specific stone
$env:GARDEN_STONE = "http://stone-02:7185"

# Set log level
$env:RUST_LOG = "debug"  # trace, debug, info, warn, error
```

## Legacy Endpoints (Still Supported)

```bash
GET  /api/services                      # List services
GET  /api/services/:service             # Get service
POST /api/operations/offer/:offering    # Install service
POST /api/operations/rest/:service      # Stop service
POST /api/operations/wake/:service      # Start service
POST /api/operations/upgrade/:service   # Upgrade service
POST /api/operations/remove/:target     # Remove service
GET  /capabilities                      # Stone capabilities
GET  /health                           # Health check
GET  /metrics                          # Metrics snapshot
```

## Status Codes

| Code | Meaning | Common Cause |
|------|---------|--------------|
| 200 | OK | Success |
| 202 | Accepted | Job queued |
| 404 | Not Found | Service/stone not found |
| 501 | Not Implemented | Phase 1.4/3 stub |
| 503 | Service Unavailable | Lantern not configured |

---

For detailed documentation, see `IMPLEMENTATION-COMPLETE.md`
