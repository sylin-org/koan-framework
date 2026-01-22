# Zen Garden Offering Manifests

Offering manifests define services that can be deployed in multiple modes: **Managed** (containers), **Adopted** (existing installations), or **Borrowed** (external network services).

## Directory Structure

```
offerings/
├── ai/             - AI/ML services (Ollama, etc.)
├── data/           - Databases (PostgreSQL, MongoDB, Redis)
├── network/        - Network services (NAS, printers, etc.)
└── README.md       - This file
```

## Offering Modes

### Managed Mode (Container-Based)
Traditional container deployments managed by Zen Garden:
- Docker image + configuration
- Volume management
- Port mapping
- Lifecycle control (start/stop/restart)

**Example**: PostgreSQL container managed by Moss

### Adopted Mode (Existing Services)
Services already running on the host (native or containerized):
- Auto-detection via commands, HTTP probes, or container inspection
- Optional lifecycle control
- Health monitoring
- Zero-downtime integration

**Example**: Native Ollama installation detected and adopted

### Borrowed Mode (External Network Services)
Services running elsewhere on the network:
- Location-based (host:port)
- Network health monitoring
- Credential management
- Connection templates

**Example**: NAS storage at `nas.local:445`

## Manifest Tiers

### Tier 1: Minimal (4-6 lines)
Bare minimum for adopted/borrowed offerings:
```yaml
name: ollama
category: ai
description: Ollama AI runtime
modes: [adopted]
detection:
  - method: command
    config:
      command: ollama --version
```

### Tier 2: Standard (10-20 lines)
Adds health monitoring and control:
```yaml
name: ollama
category: ai
description: Ollama AI runtime
modes: [adopted]
detection:
  - method: command
    config:
      command: ollama --version
control:
  level: monitor
  health_check_url: http://localhost:11434/api/tags
health:
  method: http
  endpoint: http://localhost:11434/api/tags
  interval_secs: 30
```

### Tier 3: Multi-Mode (30-50 lines)
Supports multiple deployment modes:
```yaml
name: postgresql
category: database
description: PostgreSQL database
modes:
  - managed   # Container deployment
  - adopted   # Native installation
image: postgres:16-alpine
ports:
  - host: 5432
    container: 5432
detection:
  - method: command
    config:
      command: psql --version
# ... full configuration
```

## Control Levels

### Full
Complete lifecycle management:
- Start/stop/restart commands
- Configuration updates
- Log access
- Metrics collection

**Best for**: Services you fully control (native installs, dev environments)

### Monitor (Default)
Health monitoring only:
- Periodic health checks
- Status reporting
- Automatic recovery detection

**Best for**: Most adopted services

### Announce
Discovery only:
- Register service location
- No health monitoring
- Minimal overhead

**Best for**: Stable services, borrowed resources

## Auto-Adoption

Services with `adopted` mode are automatically detected and adopted when:
1. Detection succeeds (command, HTTP probe, or container found)
2. Stability threshold met (default: 2 consecutive detections)
3. Not in exclusion list
4. Auto-adoption enabled (default for regular deployments)

### Configuration

```toml
[adoption]
enabled = true                    # Auto-adoption on/off
scan_interval_secs = 300          # 5 minutes
stability_threshold = 2           # Consecutive successes
exclusions = ["service-name"]     # Never auto-adopt
default_control_level = "monitor" # "full", "monitor", "announce"
```

### Deployment Profiles

**Regular**: Auto-adoption enabled (desktop, server)
**USB**: Auto-adoption disabled (portable, self-contained)
**Container**: Auto-adoption disabled (no host access)

## Detection Methods

### Command Execution
```yaml
detection:
  - method: command
    config:
      command: mongod --version
      expected_pattern: "v(\\d+\\.\\d+\\.\\d+)"
      expected_exit_code: 0
```

### HTTP Probe
```yaml
detection:
  - method: http_probe
    config:
      url: http://localhost:11434/api/tags
      expected_status: 200
      version_pattern: "version\":\"(.*?)\""
```

### Container Inspection
```yaml
detection:
  - method: container_inspect
    config:
      container_pattern: "postgres|pg_"
      image_pattern: "postgres:.*"
```

## Health Monitoring

### TCP Check
```yaml
health:
  method: tcp
  endpoint: localhost:5432
  interval_secs: 30
```

### HTTP Check
```yaml
health:
  method: http
  endpoint: http://localhost:8080/health
  interval_secs: 30
```

### Command Check
```yaml
health:
  method: command
  command: pg_isready -h localhost
  interval_secs: 30
```

## Secrets Management

Borrowed offerings can reference stored credentials:

```yaml
name: private-db
category: database
modes: [borrowed]
location:
  host: db.internal
  port: 5432
  protocol: postgresql
credentials_key: "private-db-creds"  # Stored in encrypted backend
```

Store credentials via API:
```bash
curl -X POST http://localhost:7190/api/v1/secrets/private-db-creds \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "secret"}'
```

## API Endpoints

### List Adoptable Offerings
```bash
GET /api/v1/offerings/adoptable
```
Returns services detected but not yet adopted.

### Manually Adopt Offering
```bash
POST /api/v1/offerings/:offering/adopt
{
  "control_level": "monitor",
  "location": "localhost",
  "port": 11434
}
```

### List Adopted Offerings
```bash
GET /api/v1/offerings/adopted
```

### List Borrowed Offerings
```bash
GET /api/v1/offerings/borrowed
```

### Unadopt Offering
```bash
DELETE /api/v1/offerings/:offering/adopt
```

## Examples

### Minimal Adopted: Ollama
```yaml
name: ollama
category: ai
description: Ollama AI runtime
modes: [adopted]
detection:
  - method: command
    config:
      command: ollama --version
```

### Multi-Mode: PostgreSQL
```yaml
name: postgresql
category: database
description: PostgreSQL database
modes: [managed, adopted]
image: postgres:16-alpine
ports:
  - host: 5432
    container: 5432
detection:
  - method: command
    config:
      command: psql --version
```

### Borrowed: NAS Storage
```yaml
name: nas-storage
category: storage
description: Network storage
modes: [borrowed]
location:
  host: nas.local
  port: 445
  protocol: smb
connection_template: "smb://{{host}}/share"
```

## Schema Reference

See [common/src/manifests/offering.rs](../src/common/src/manifests/offering.rs) for complete schema definition.

### Required Fields
- `name`: Offering identifier (unique)
- `category`: Service category
- `description`: Human-readable description
- `modes`: List of supported modes

### Optional Fields (All)
All other fields are optional and omitted when not needed:
- `image`, `ports`, `environment`, `volumes` (managed mode)
- `detection`, `control` (adopted mode)
- `location`, `health`, `connection_template` (borrowed mode)

### Validation
- Optional fields completely omitted (not null/{}/[])
- Minimal manifests: 4-6 lines for Tier 1
- No hardcoded service names in code
- All examples in manifests/tests only

## Migration from Old Manifests

Old docker-compose snippets in `manifests/` are for legacy `garden-rake offer` command.

New offering manifests in `offerings/` support multi-mode deployments and are used by Moss daemon.

To migrate:
1. Create new offering manifest in `offerings/`
2. Add `modes: [managed]` for container deployment
3. Copy image, ports, environment, volumes from old manifest
4. Add detection rules for adopted mode support (optional)
5. Test with `moss` daemon

## Testing

Manifests are automatically validated on load. Check logs for errors:
```bash
moss # Start daemon
# Check console events for manifest loading
curl http://localhost:7190/api/v1/offerings/adoptable
```

## Contributing

When adding new offerings:
1. Start with minimal Tier 1 manifest
2. Test auto-detection thoroughly
3. Add health monitoring for production use
4. Document any special requirements
5. Follow naming convention: `category/service-name.yaml`
