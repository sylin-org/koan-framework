---
type: REF
domain: orchestration
title: "Orchestration Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/reference/orchestration/index.md
---

# Orchestration Pillar Reference

**Document Type**: REF
**Target Audience**: Developers, Architects, AI Agents
**Last Updated**: 2025-09-28
**Framework Version**: v0.6.2

---

## Installation

```bash
dotnet tool install --global Koan.CLI
```

## Basic Commands

### Start Development Environment

```bash
# Start all dependencies
Koan up

# Start with specific engine
Koan up --engine docker
Koan up --engine podman

# Start with verbose output
Koan up -v

# Start with custom timeout
Koan up --timeout 300
```

### Environment Management

```bash
# Check status
Koan status

# View logs
Koan logs

# Stop environment
Koan down

# Stop and remove data
Koan down --volumes
```

### Export Artifacts

```bash
# Export Docker Compose
Koan export compose

# Export for CI
Koan export compose --profile ci

# Export for production
Koan export compose --profile prod
```

## Configuration

### Service Declaration

```csharp
[KoanService("postgres", Image = "postgres:15", Ports = [5432])]
public class PostgresService
{
    [DefaultEndpoint]
    public static string ConnectionString => "Host=localhost;Port=5432;Database=app;Username=postgres;Password=postgres";

    [HostMount("/var/lib/postgresql/data")]
    public static string DataPath => "./Data/postgres";
}
```

### Application Service

```csharp
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddKoan();

        var app = builder.Build();

        app.UseKoan();
        app.Run();
    }
}

// Koan.orchestration.yml (optional overrides)
services:
  app:
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
```

## Environment Profiles

### Local Development

```bash
export Koan_ENV=local
Koan up
```

**Profile Characteristics:**
- Conservative timeouts
- Bind mounts enabled
- Optional telemetry exporters
- Seeders allowed

### CI/CD

```bash
export Koan_ENV=ci
Koan up
```

**Profile Characteristics:**
- Ephemeral named volumes
- Deterministic ports with auto-avoid
- Faster failure timeouts
- No bind mounts

### Staging

```bash
export Koan_ENV=staging
Koan export compose --profile staging
```

**Profile Characteristics:**
- Export-only (no local execution)
- Bind mounts for persistence
- Production-like configuration

### Production

```bash
export Koan_ENV=prod
Koan export compose --profile prod
```

**Profile Characteristics:**
- Export-only
- No automatic mount injection
- Strict port conflict handling

## Port Management

### Automatic Port Assignment

```csharp
[KoanService("redis", Image = "redis:7", Ports = [6379])]
public class RedisService
{
    // Framework assigns available host port automatically
    [DefaultEndpoint]
    public static string ConnectionString => "localhost:6379";
}
```

### Custom Port Configuration

```bash
# Use specific base port
Koan up --base-port 9000

# Use specific app port
Koan up --port 8080

# Handle port conflicts
Koan up --conflicts fail  # Fail on conflicts
Koan up --conflicts warn  # Warn but continue
```

### Port Conflict Resolution

```bash
# Check port availability
Koan status

# View port assignments
Koan status --json
```

## Service Dependencies

### Health Checks

```csharp
[KoanService("postgres", Image = "postgres:15")]
public class PostgresService
{
    [HealthCheck]
    public static async Task<bool> IsHealthy()
    {
        try
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

### Dependency Ordering

```yaml
# Koan.orchestration.yml
services:
  app:
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_started
```

## Data Persistence

### Automatic Mounts

```csharp
[KoanService("postgres", Image = "postgres:15")]
public class PostgresService
{
    // Automatic mount based on profile
    [HostMount("/var/lib/postgresql/data")]
    public static string DataPath => "./Data/postgres";
}
```

**Mount Behavior by Profile:**
- **Local/Staging**: `./Data/postgres:/var/lib/postgresql/data`
- **CI**: `data_postgres:/var/lib/postgresql/data` (named volume)
- **Production**: No automatic mounts

### Custom Mount Configuration

```yaml
# Koan.orchestration.yml
services:
  postgres:
    volumes:
      - "./custom-data:/var/lib/postgresql/data"
      - "./init-scripts:/docker-entrypoint-initdb.d"
```

## Environment Variables

### Service Environment

```csharp
[KoanService("postgres", Image = "postgres:15")]
public class PostgresService
{
    [Environment("POSTGRES_DB")]
    public static string Database => "myapp";

    [Environment("POSTGRES_USER")]
    public static string Username => "postgres";

    [Environment("POSTGRES_PASSWORD")]
    public static string Password => "postgres";
}
```

### Application Environment

```yaml
# Koan.orchestration.yml
services:
  app:
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - Koan__Data__DefaultProvider=Postgres
      - Koan__Data__Postgres__ConnectionString=${POSTGRES_CONNECTION}
```

## Networking

### Internal Network

```csharp
// Services communicate using internal network
[KoanService("redis", Image = "redis:7")]
public class RedisService
{
    // Internal hostname
    [DefaultEndpoint]
    public static string InternalConnectionString => "redis:6379";

    // External endpoint for development
    [DefaultEndpoint("external")]
    public static string ExternalConnectionString => "localhost:6379";
}
```

### Exposed Services

```bash
# Expose internal services for debugging
Koan up --expose-internals
```

## Advanced Configuration

### Custom Service Definition

```csharp
[KoanService("ollama", Image = "ollama/ollama:latest")]
public class OllamaService
{
    [DefaultEndpoint]
    public static string BaseUrl => "http://localhost:11434";

    [HostMount("/root/.ollama")]
    public static string ModelsPath => "./Data/ollama";

    [Environment("OLLAMA_HOST")]
    public static string Host => "0.0.0.0:11434";

    [HealthCheck("/api/tags")]
    public static string HealthEndpoint => "/api/tags";
}
```

### Multi-Container Services

```yaml
# Koan.orchestration.yml
services:
  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.0.0
    environment:
      - discovery.type=single-node
      - xpack.security.enabled=false
    ports:
      - "9200:9200"
    volumes:
      - elasticsearch_data:/usr/share/elasticsearch/data

  kibana:
    image: docker.elastic.co/kibana/kibana:8.0.0
    environment:
      - ELASTICSEARCH_HOSTS=http://elasticsearch:9200
    ports:
      - "5601:5601"
    depends_on:
      - elasticsearch

volumes:
  elasticsearch_data:
```

## Monitoring and Diagnostics

### Health Monitoring

```bash
# Check overall health
Koan status

# Get detailed health info
Koan status --json

# Check specific service
Koan logs --service postgres

# Follow logs in real-time
Koan logs --follow
```

### Diagnostic Tools

```bash
# System diagnostics
Koan doctor

# System diagnostics (JSON)
Koan doctor --json

# Explain what would happen
Koan up --explain

# Validate configuration without running
Koan up --dry-run
```

### Engine Detection

```bash
# Check available engines
Koan doctor

# Force specific engine
Koan up --engine docker
Koan up --engine podman
```

## Generated Artifacts

### Docker Compose

```yaml
# .Koan/compose.yml (generated)
version: '3.8'

services:
  postgres:
    image: postgres:15
    environment:
      POSTGRES_DB: myapp
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - ./Data/postgres:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 30s
      timeout: 10s
      retries: 3
    networks:
      - koan_internal

  app:
    image: myapp:latest
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - Koan__Data__Postgres__ConnectionString=Host=postgres;Database=myapp;Username=postgres;Password=postgres
    networks:
      - koan_internal
      - koan_external

networks:
  koan_internal:
    internal: true
  koan_external:
```

### Kubernetes (Future)

```yaml
# .Koan/k8s/postgres.yml (planned)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres
  template:
    metadata:
      labels:
        app: postgres
    spec:
      containers:
      - name: postgres
        image: postgres:15
        env:
        - name: POSTGRES_DB
          value: myapp
        ports:
        - containerPort: 5432
        volumeMounts:
        - name: postgres-data
          mountPath: /var/lib/postgresql/data
      volumes:
      - name: postgres-data
        persistentVolumeClaim:
          claimName: postgres-pvc
```

## Troubleshooting

### Common Issues

```bash
# Port conflicts
Koan up --conflicts fail

# Timeout issues
Koan up --timeout 600

# Engine not found
Koan doctor

# Services not ready
Koan status
Koan logs --tail 200
```

### Engine-Specific Commands

```bash
# Docker debugging
docker compose -f .Koan/compose.yml ps
docker compose -f .Koan/compose.yml logs --tail=200

# Podman debugging
podman compose -f .Koan/compose.yml ps
podman compose -f .Koan/compose.yml logs --tail=200
```

### Configuration Validation

```bash
# Validate without side effects
Koan up --dry-run

# See planned configuration
Koan up --explain

# Check manifest discovery
Koan inspect
```

## API Reference

### Service Attributes

```csharp
[KoanService(string id, string? Image = null, int[]? Ports = null)]
public class ServiceAttribute : Attribute { }

[DefaultEndpoint(string? name = null)]
public class DefaultEndpointAttribute : Attribute { }

[HostMount(string containerPath)]
public class HostMountAttribute : Attribute { }

[Environment(string key)]
public class EnvironmentAttribute : Attribute { }

[HealthCheck(string? endpoint = null)]
public class HealthCheckAttribute : Attribute { }
```

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error |
| 2 | Configuration error |
| 3 | Engine not available |
| 4 | Readiness timeout |
| 5 | Port conflict (when --conflicts fail) |

### Environment Variables

```bash
# Profile selection
Koan_ENV=local|ci|staging|prod

# Port management
Koan_PORT_PROBE_MAX=200

# Engine selection
Koan_ENGINE=docker|podman

# Timeout overrides
Koan_TIMEOUT=300
```

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+