# Koan Aspire Integration - Startup Guide

This sample demonstrates Koan Framework's self-orchestration capabilities and Docker Compose integration. You can run it in two different modes:

## 🚀 Quick Start

### Option 1: Standalone Mode (Recommended for Development)
**Uses Koan's self-orchestration to automatically manage dependencies**

```bash
# Windows
start-standalone.bat

# Manual command
dotnet run --urls http://localhost:8080
```

**What happens:**
- ✅ Application starts immediately
- ✅ Self-orchestration detects Postgres + Redis dependencies
- ✅ Automatically starts Docker containers with proper networking
- ✅ Waits for dependencies to be healthy before continuing
- ✅ Clean automatic cleanup when application stops

### Option 2: Docker Compose Mode
**Full containerized stack with explicit orchestration**

```bash
# Windows
start-compose.bat

# Manual commands
docker compose up --build
```

**What happens:**
- 📦 Builds application Docker image
- 📦 Starts Postgres, Redis, and Application containers
- 📦 Uses health checks and dependency ordering
- 📦 All services run in isolated network

## 📡 Endpoints

Once running (either mode), the application is available at:

- **Application**: http://localhost:8080
- **Health Check**: http://localhost:8080/health
- **Swagger UI**: http://localhost:8080/swagger
- **Observability**: http://localhost:8080/.well-known/observability
- **Aggregates Discovery**: http://localhost:8080/.well-known/aggregates

## 🛠 Dependencies

### Standalone Mode Requirements
- .NET 8 SDK
- Docker Desktop (for dependency containers)

### Docker Compose Mode Requirements
- Docker Desktop with Docker Compose

## 🧹 Cleanup

### Standalone Mode
- **Automatic**: Stop application (Ctrl+C) - self-orchestration handles cleanup
- **Manual**: `docker ps` and `docker stop <container-names>` if needed

### Docker Compose Mode
```bash
# Stop services
docker compose down

# Complete cleanup (removes volumes and data)
cleanup-compose.bat
# OR
docker compose down -v --remove-orphans
```

## 🔧 Configuration

### Standalone Mode
- Self-orchestration detects dependencies and configures connections automatically
- Uses dynamic port assignment and service discovery
- Connection strings are generated based on actual container IPs/ports

### Docker Compose Mode
- Fixed service names: `postgres`, `redis`, `koan-app`
- Pre-configured connection strings in docker-compose.yml
- Environment variables override appsettings.json

## 📊 Monitoring

Both modes provide the same observability endpoints:

```bash
# Health status
curl http://localhost:8080/health

# Framework observability
curl http://localhost:8080/.well-known/observability

# Discovered aggregates
curl http://localhost:8080/.well-known/aggregates
```

## 🎯 Use Cases

**Standalone Mode** - Best for:
- Local development
- Quick testing
- Debugging
- When you want minimal Docker complexity

**Docker Compose Mode** - Best for:
- Production-like environments
- Integration testing
- Team environments with shared configurations
- When you need explicit container orchestration

## 🔍 Troubleshooting

### Common Issues

**Standalone Mode:**
- Ensure Docker Desktop is running
- Check port 8080 is available
- Self-orchestration logs show dependency discovery process

**Docker Compose Mode:**
- Ensure Docker Compose is available: `docker compose version`
- Check all required ports are available (5432, 6379, 8080)
- Review build logs: `docker compose logs koan-app`

### Logs
```bash
# Standalone mode - see console output directly

# Docker Compose mode
docker compose logs -f              # All services
docker compose logs -f koan-app     # Application only
docker compose logs -f postgres     # Database only
docker compose logs -f redis        # Cache only
```

---

**Both modes demonstrate the same Koan Framework capabilities** - choose the mode that best fits your development workflow!