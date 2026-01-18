# Zen Garden Architecture

## Overview

Zen Garden is a service discovery system for self-hosted infrastructure, consisting of three main components:

- **Moss** - Service discovery daemon running on each stone (server)
- **Rake** - Command-line tool for interacting with stones
- **Lantern** - Central registry service for topology tracking

## Project Structure

```
src/
├── common/          # Shared library (zen-common)
│   ├── types.rs         # Core data structures
│   ├── constants/       # Centralized constants with GARDEN_* env support
│   │   ├── mod.rs           # Ports, names, error codes
│   │   ├── timeouts.rs      # Timeout constants
│   │   ├── paths.rs         # File system paths
│   │   └── limits.rs        # System limits
│   ├── responses.rs     # Standardized API responses
│   ├── jobs.rs          # Retry policies and job handling
│   ├── utils.rs         # Helper functions
│   ├── net.rs           # Network utilities
│   └── errors.rs        # Error types
│
├── moss/            # Moss daemon (zen-moss)
│   └── src/
│       ├── main.rs          # Application entry point
│       ├── console.rs       # First-boot and TTY operations
│       ├── discovery.rs     # UDP discovery protocol
│       ├── docker.rs        # Docker integration
│       ├── mdns.rs          # mDNS announcement
│       ├── metrics.rs       # Resource metrics
│       └── templates.rs     # Service templates (loaded from runtime templates dir)
│
├── rake/            # Rake CLI (zen-rake)
│   └── src/
│       ├── main.rs          # CLI entry point and commands
│       ├── discovery.rs     # UDP discovery client
│       └── stone_cache.rs   # Hot cache for discovered stones
│
├── lantern/         # Lantern registry (garden-lantern)
│   └── src/
│       ├── main.rs          # HTTP server
│       ├── registry.rs      # Service registry logic
│       └── state.rs         # Application state
│
└── build-utils/     # Build-time utilities (zen-build-utils)
    └── src/
        └── lib.rs           # Build number capture
```

## Architecture Principles

### 1. Layered Design

Components follow a layered architecture pattern:

- **API Layer**: HTTP endpoints (Axum), CLI commands (Clap)
- **Domain Layer**: Business logic, compatibility checking, job processing
- **Infrastructure Layer**: Docker, mDNS, filesystem, networking

### 2. Standardization Layer

The `zen-common` crate provides:

- **Constants with Environment Overrides**: All timeouts, paths, and limits can be overridden via `GARDEN_*` environment variables
  - Example: `GARDEN_DISCOVERY_TIMEOUT_SECS` overrides default 3s discovery timeout
  - Example: `GARDEN_CACHE_TTL_SECS` overrides default 90s cache TTL

- **Standardized Responses**: `ApiResponse<T>` and `ApiError` for consistent HTTP responses

- **Retry Policies**: `RetryPolicy` with exponential backoff for resilient operations
  - `retry_with_policy()` for async operations
  - `retry_simple()` for basic retry needs

- **Centralized Error Codes**: Consistent error codes mapped to HTTP status codes

### 3. Build System

- **Build Numbers**: Timestamp-based versioning (yyyyMMdd.HHmm)
  - Set via `CARGO_BUILD_NUMBER` environment variable
  - Build scripts in `build-dist.ps1`, `build-linux.ps1`, `build-windows.ps1`
  - Captured by `zen-build-utils::capture_build_number()`

- **Cross-Platform**: 
  - Windows: Native builds
  - Linux: Docker-based builds with cross-compilation

### 4. Discovery Mechanisms

1. **UDP Broadcast** (Port 7184):
   - Fast local network discovery
   - Used by rake to find moss instances
   - 2-3 second timeout

2. **mDNS** (Avahi):
   - Advertises stones as `<stone-name>.local`
   - Enables DNS-based discovery
   - Automatic on Linux systems

3. **Lantern Registry** (Port 7186):
   - Central topology tracking
   - 90-second TTL with heartbeats
   - Peer stone queries

### 5. First-Boot Architecture

Moss performs first-boot initialization as a **background task**:

- Generates stone name if not configured
- Sets hostname
- Creates MOTD
- Tests mDNS resolution
- Flag file: `/etc/zen-garden/.first-run-complete`
- Retry strategy: 20 attempts × 3s delays = 60s window
- Exits after completion to trigger systemd restart

### 6. Cache Strategy

**Hot Cache Architecture** (per zen-garden philosophy):
- Stone discovery results cached for 90 seconds
- Reduces network traffic
- Enables fast repeated operations

## Key Design Decisions

### ADRs (Architecture Decision Records)

Documented in `docs/decisions/`:

- **ARCH-0040**: Config and Constants Naming
- **DATA-0061**: Data Access Pagination and Streaming
- **WEB-0035**: EntityController Transformers

### Critical Paths

1. **Runtime Templates**: Offerings are loaded from disk at runtime
   - Path: `/etc/zen-garden/templates` (on stones)
   - Behavior: If the runtime templates directory is missing/empty, offering list/info/install will fail

2. **Service Compatibility**: CPU architecture validation before deployment
   - Checks ELF headers
   - Applies fallback images for incompatible hardware

3. **Resource Monitoring**: Real-time metrics collection
   - CPU, memory, disk, network
   - Per-container resource tracking

## Technology Stack

- **Language**: Rust 2021 (MSRV 1.75)
- **HTTP Server**: Axum 0.7
- **Async Runtime**: Tokio 1.35
- **CLI**: Clap 4.4
- **Container Management**: Bollard (Docker client)
- **Service Discovery**: mdns-sd 0.7
- **Serialization**: Serde + serde_json + serde_yaml

Note: Lantern may embed static web assets at compile time; this is separate from moss offerings.

## Testing

- **Unit Tests**: 32 tests across workspace
- **Integration Tests**: Discovery, registry, cache tests
- **Doctests**: Build utilities, common types

Run tests:
```bash
cargo test --workspace
```

## Building

### Development
```bash
cargo build --workspace
```

### Release
```bash
# Windows
.\installer\build-windows.ps1

# Linux (via Docker)
.\installer\build-linux.ps1

# Distribution package
.\installer\dist.ps1
```

### Deployment
```bash
# Push to all stones
.\installer\push-moss-to-all-stones.ps1
```

## Environment Variables

### Runtime Configuration

- `GARDEN_DISCOVERY_TIMEOUT_SECS` - Discovery timeout (default: 3)
- `GARDEN_CACHE_TTL_SECS` - Cache TTL (default: 90)
- `GARDEN_HTTP_REQUEST_TIMEOUT_SECS` - HTTP timeout (default: 30)
- `GARDEN_CONFIG_DIR` - Config directory (default: /etc/zen-garden)
- `GARDEN_STONE_HOME` - Stone home directory (default: /home/stone)

See `src/common/src/constants/` for full list.

### Build Configuration

- `CARGO_BUILD_NUMBER` - Build timestamp (e.g., "20260117.0156")
- `RUST_LOG` - Logging level (trace/debug/info/warn/error)

## Monitoring

### Health Checks

- **Moss**: `GET http://<stone>:7185/api/health`
- **Lantern**: `GET http://<lantern>:7186/api/health`

### Event Streaming

- **Moss Events**: `GET http://<stone>:7185/api/events` (SSE)
- **Container Logs**: `GET http://<stone>:7185/api/services/:name/logs` (SSE)

## Future Enhancements

- **Layered Refactoring**: Split moss/rake main.rs into api/domain/infra modules
- **Pond Security**: mTLS and authentication (currently scaffolded)
- **Advanced Scheduling**: Resource-aware service placement

## References

- **Documentation**: `docs/`
- **Engineering Guidelines**: `docs/engineering/`
- **API Specifications**: `docs/api/`
- **Samples**: `samples/`
