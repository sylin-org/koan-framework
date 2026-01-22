# Offering Manifests Quick Start

Get started with offering manifests in 5 minutes.

## 1. Choose Your Mode

### Adopted Mode (Existing Service)
Use when the service is already installed on your system.

**Example**: Ollama AI runtime installed via package manager

### Managed Mode (Container)
Use when you want Zen Garden to deploy and manage containers.

**Example**: PostgreSQL database in Docker

### Borrowed Mode (External Network)
Use when the service is running elsewhere on your network.

**Example**: NAS storage at `nas.local:445`

---

## 2. Create Minimal Manifest

### Adopted (4-6 lines)
```yaml
name: my-service
category: ai
description: My AI service
modes: [adopted]
detection:
  - method: command
    config:
      command: my-service --version
```

### Managed (8-12 lines)
```yaml
name: my-db
category: database
description: My database
modes: [managed]
image: my-db:latest
ports:
  - host: 5432
    container: 5432
volumes:
  - host: my-db-data
    container: /var/lib/data
```

### Borrowed (6-8 lines)
```yaml
name: my-nas
category: storage
description: My network storage
modes: [borrowed]
location:
  host: nas.local
  port: 445
  protocol: smb
```

---

## 3. Save to Offerings Directory

```bash
# Choose appropriate category
offerings/
  ai/           - AI/ML services
  data/         - Databases
  network/      - Network devices/services
  cache/        - Caching services
  messaging/    - Message brokers
  storage/      - Storage systems
  compute/      - Compute resources

# Save your manifest
vi offerings/ai/my-service.yaml
```

---

## 4. Start Moss & Verify

```bash
# Start daemon
cd other/zen-garden/src/moss
cargo run

# Look for log output
# [INFO] Loading offering manifests...
# [DEBUG] Loaded offering manifest: my-service (modes: [adopted])
# [INFO] 6 offering manifests loaded

# Query via API
curl http://localhost:7190/api/v1/offerings/adoptable
```

---

## 5. Add Health Monitoring (Optional)

```yaml
name: my-service
category: ai
description: My AI service
modes: [adopted]
detection:
  - method: command
    config:
      command: my-service --version

# Add health monitoring
health:
  method: http
  endpoint: http://localhost:8080/health
  interval_secs: 30

# Add control configuration
control:
  level: monitor
  health_check_url: http://localhost:8080/health
```

---

## Common Patterns

### Command Detection with Version Extraction
```yaml
detection:
  - method: command
    config:
      command: my-service --version
      expected_pattern: "version (\\d+\\.\\d+\\.\\d+)"
```

### HTTP Probe Detection
```yaml
detection:
  - method: http_probe
    config:
      url: http://localhost:8080/api/version
      expected_status: 200
      version_pattern: "\"version\":\"(.*?)\""
```

### Container Detection
```yaml
detection:
  - method: container_inspect
    config:
      container_pattern: "my-service|myservice"
      image_pattern: "my-org/my-service:.*"
```

### Multiple Detection Methods (OR logic)
```yaml
detection:
  - method: command
    config:
      command: my-service --version
  - method: http_probe
    config:
      url: http://localhost:8080/health
  - method: container_inspect
    config:
      container_pattern: "my-service"
```

---

## Control Levels

### Monitor (Default - Recommended)
```yaml
control:
  level: monitor
  health_check_url: http://localhost:8080/health
```

Health monitoring only, no lifecycle control.

### Full
```yaml
control:
  level: full
  start_command: systemctl start my-service
  stop_command: systemctl stop my-service
  restart_command: systemctl restart my-service
  health_check_url: http://localhost:8080/health
```

Complete lifecycle management.

### Announce
```yaml
control:
  level: announce
```

Discovery only, no monitoring.

---

## Multi-Mode Offerings

Support both container and native deployments:

```yaml
name: postgresql
category: database
description: PostgreSQL database
modes:
  - managed   # Container deployment
  - adopted   # Native installation

# Managed mode config
image: postgres:16-alpine
ports:
  - host: 5432
    container: 5432
environment:
  - name: POSTGRES_PASSWORD
    value: postgres

# Adopted mode config
detection:
  - method: command
    config:
      command: psql --version
  - method: container_inspect
    config:
      container_pattern: "postgres"
```

---

## Validation Checklist

Before deploying:

- [ ] Name is unique (no conflicts with existing offerings)
- [ ] Category is appropriate
- [ ] At least one mode specified
- [ ] Detection rules present for adopted mode
- [ ] Image specified for managed mode
- [ ] Location specified for borrowed mode
- [ ] Health monitoring configured (recommended)
- [ ] Control level appropriate for use case
- [ ] Optional fields omitted (not null/empty)
- [ ] YAML syntax valid

---

## Testing Your Manifest

```bash
# 1. Check daemon logs for loading
cargo run
# Look for: [DEBUG] Loaded offering manifest: my-service

# 2. Query adoptable offerings
curl http://localhost:7190/api/v1/offerings/adoptable
# Should include your service if detected

# 3. Manually adopt
curl -X POST http://localhost:7190/api/v1/offerings/my-service/adopt \
  -H "Content-Type: application/json" \
  -d '{"control_level": "monitor"}'

# 4. Verify adoption
curl http://localhost:7190/api/v1/offerings/adopted
```

---

## Common Issues

### "Manifest not loaded"
- Check YAML syntax (indentation, quotes)
- Verify file extension (.yaml or .yml)
- Check daemon logs for parse errors

### "Service not detected"
- Run detection command manually
- Check expected_pattern regex
- Verify service is actually running
- Check detection method is appropriate

### "Auto-adoption not working"
- Check `adoption.enabled = true` in config
- Verify stability_threshold met (default: 2)
- Check service not in exclusions list
- Look for "Detected but not yet stable" in logs

---

## Examples Repository

See existing manifests for reference:
- `ai/ollama.yaml` - Minimal adopted (Tier 1)
- `data/postgresql.yaml` - Multi-mode with full config
- `data/mongodb.yaml` - Multi-mode database
- `network/nas-storage.yaml` - Borrowed network service
- `network/network-printer.yaml` - Borrowed device

---

## Advanced Features

### Secrets Management
```yaml
name: private-db
category: database
modes: [borrowed]
location:
  host: db.internal
  port: 5432
  protocol: postgresql
credentials_key: "private-db-creds"
```

Store credentials via API:
```bash
curl -X POST http://localhost:7190/api/v1/secrets/private-db-creds \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "secret"}'
```

### Connection Templates
```yaml
connection_template: "postgresql://{{host}}:{{port}}/{{database}}"
```

Clients can use this template to connect.

### Custom Stability Threshold
```yaml
detection:
  - method: command
    config:
      command: my-service --version
    stability_threshold: 5  # Require 5 consecutive detections
```

### Custom Cache TTL
```yaml
detection:
  - method: http_probe
    config:
      url: http://localhost:8080/health
    cache_ttl_secs: 60  # Cache detection result for 1 minute
```

---

## Next Steps

1. **Create your first manifest** using patterns above
2. **Test detection** manually before relying on auto-adoption
3. **Add health monitoring** for production use
4. **Review logs** to troubleshoot issues
5. **Read full docs** at [offerings/README.md](README.md)

## Getting Help

- **Full documentation**: `offerings/README.md`
- **Schema reference**: `common/src/manifests/offering.rs`
- **API docs**: `moss/src/api/v1/adoption.rs`
- **Examples**: All files in `offerings/` directory

Happy offering manifesting! 🌱
