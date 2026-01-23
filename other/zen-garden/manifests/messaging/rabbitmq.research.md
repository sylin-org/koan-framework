# RabbitMQ Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | RabbitMQ |
| **Category** | Message Broker |
| **Primary Use** | Message queuing, pub/sub, task distribution, event streaming |
| **License** | Mozilla Public License 2.0 |
| **Project URL** | https://www.rabbitmq.com/ |
| **Docker Hub** | https://hub.docker.com/_/rabbitmq |
| **Runtime** | Erlang/OTP |

## Docker Image Analysis

### Image Selection
**Selected**: `rabbitmq:3-management-alpine`

This image provides:
- RabbitMQ 3.x (latest stable)
- Management UI plugin pre-enabled
- Alpine base for smaller image size (~50MB compressed)

### Version Strategy

| Tag | Size | Features | Use Case |
|-----|------|----------|----------|
| `rabbitmq:3` | ~150MB | Base only | Minimal deployments |
| `rabbitmq:3-alpine` | ~50MB | Base, Alpine | Size-constrained |
| `rabbitmq:3-management` | ~180MB | Base + UI | Development/monitoring |
| **`rabbitmq:3-management-alpine`** | ~70MB | Base + UI, Alpine | **Recommended** |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64v8 | ✅ | Erlang JIT support (25+) |
| arm32v7 | ✅ | Raspberry Pi 2/3 |
| arm32v6 | ✅ | Raspberry Pi Zero/1 |
| i386 | ✅ | Legacy 32-bit x86 |
| ppc64le | ✅ | IBM Power |
| riscv64 | ✅ | RISC-V |
| s390x | ✅ | IBM Z |

**Conclusion**: Excellent multi-architecture support. No fallback images needed.

**Sources**:
- [Docker Hub rabbitmq](https://hub.docker.com/_/rabbitmq)
- [Docker Hub arm64v8/rabbitmq](https://hub.docker.com/r/arm64v8/rabbitmq)

## CPU Compatibility

### No AVX/SSE Requirements

**RabbitMQ does NOT require AVX, SSE, or other specialized CPU instructions.**

The Erlang VM is written for portability across platforms. RabbitMQ successfully runs on:
- Intel Celeron J-series (no AVX)
- Raspberry Pi (all models, including Pi Zero)
- ARM servers (AWS Graviton, etc.)
- Apple Silicon (M1/M2/M3)

### Erlang JIT on ARM64

Starting with Erlang 25, the JIT compiler supports ARM64, providing significant performance improvements on:
- Raspberry Pi 4/5 (64-bit OS)
- AWS Graviton instances
- Apple Silicon Macs

The official Docker images include JIT-enabled Erlang on arm64.

**Sources**:
- [RabbitMQ Supported Platforms](https://www.rabbitmq.com/docs/platforms)
- [RabbitMQ 3.10 Performance Improvements](https://www.rabbitmq.com/blog/2022/05/16/rabbitmq-3.10-performance-improvements)

## Resource Requirements

### Memory

| Component | Memory | Notes |
|-----------|--------|-------|
| Erlang VM base | 128MB | Minimum for runtime |
| Per connection | ~100KB | Add per client connection |
| Per queue | Variable | Depends on messages |
| Message overhead | ~1KB/msg | Metadata + AMQP properties |

**Memory Watermark**: By default, RabbitMQ uses 60% of available RAM before raising memory alarms and blocking publishers.

| Workload | Memory | Notes |
|----------|--------|-------|
| Minimum | 512MB | Bare operation, few connections |
| Recommended | 1-2GB | Small deployments, <1000 msg/s |
| Production | 4GB+ | High throughput, many connections |

**Important**: Erlang GC can temporarily double memory usage during garbage collection. Plan for headroom.

**Formula**: Total = 128MB + (connections × 100KB) + (queued_messages × 1KB) + 30% buffer

### CPU

| Requirement | Value |
|-------------|-------|
| Minimum Cores | 1 |
| Recommended | 2-4 |
| High throughput | 4-8+ |

RabbitMQ can handle ~10K messages/second on modest hardware (4 cores, 4-8GB RAM) with messages under 4KB.

### Disk

| Requirement | Value |
|-------------|-------|
| Minimum | 50MB |
| With persistence | Depends on queue depth |
| Volume mount | `/var/lib/rabbitmq` |

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 5672 | TCP | AMQP 0-9-1 and AMQP 1.0 |
| 15672 | HTTP | Management UI (if enabled) |
| 25672 | TCP | Erlang distribution (clustering) |
| 4369 | TCP | epmd (Erlang Port Mapper) |
| 5671 | TCP | AMQPS (AMQP over TLS) |
| 15671 | HTTPS | Management UI over TLS |
| 1883 | TCP | MQTT (if plugin enabled) |
| 61613 | TCP | STOMP (if plugin enabled) |

**For basic use**: Only 5672 (AMQP) and 15672 (Management) need to be exposed.

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "rabbitmq-diagnostics", "ping"]
  interval: 15s
  timeout: 10s
  retries: 5
```

**Why `rabbitmq-diagnostics ping`**:
- Simplest health check
- Verifies runtime is running
- Verifies authentication works
- Built into RabbitMQ

### Alternative Health Checks

```bash
# More comprehensive check
rabbitmq-diagnostics -q check_running && rabbitmq-diagnostics -q check_local_alarms

# Check port connectivity
rabbitmq-diagnostics check_port_connectivity

# Simple TCP check (lowest overhead)
nc -z localhost 5672
```

### Performance Note

CLI commands like `rabbitmq-diagnostics` have overhead because they join/leave the Erlang distribution cluster. For high-frequency checks, consider TCP port checks instead.

**Best Practice** (from Kubernetes Operator): Use TCP port check on AMQP port (5672) for readiness, no liveness probe.

**Sources**:
- [RabbitMQ Monitoring](https://www.rabbitmq.com/docs/monitoring)
- [rabbitmq-diagnostics man page](https://www.rabbitmq.com/docs/man/rabbitmq-diagnostics.8)

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `RABBITMQ_DEFAULT_USER` | `guest` | Default username |
| `RABBITMQ_DEFAULT_PASS` | `guest` | Default password |
| `RABBITMQ_DEFAULT_VHOST` | `/` | Default virtual host |
| `RABBITMQ_ERLANG_COOKIE` | Random | Cluster authentication |
| `RABBITMQ_NODENAME` | Generated | Node name |

**Security Note**: Default `guest` user can only connect from localhost. For external connections, create additional users.

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 512MB RAM | memory_mb_less_than: 512 | Fail | Erlang VM minimum + overhead |
| < 1GB RAM | memory_mb_less_than: 1024 | Warning | Production workloads need headroom |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `Cannot allocate memory\|OOM` | Out of memory | Increase RAM |
| `memory resource limit alarm` | Watermark exceeded | Increase RAM or adjust watermark |
| `epmd error\|ERLANG_COOKIE` | Clustering/distribution issue | Check Erlang cookie |
| `mnesia.*failed to start` | Database corruption | Check permissions or clear data |

## Raspberry Pi Performance

| Device | Memory | Notes |
|--------|--------|-------|
| Pi Zero W | 512MB | Works, very limited |
| Pi 3B | 1GB | Suitable for light use |
| Pi 4 (4GB) | 4GB | Good for small deployments |
| Pi 4 (8GB) | 8GB | Recommended for production |
| Pi 5 | 4-8GB | Best ARM performance |

RabbitMQ clusters have been successfully run on Raspberry Pi Zero devices.

**Sources**:
- [How to Build a RabbitMQ Cluster on Raspberry Pi](https://medium.com/swlh/how-to-build-a-rabbitmq-cluster-with-a-few-tiny-raspberry-pi-zeros-e5ffb3920e40)
- [RabbitMQ on Raspberry Pi for IoT](https://fleetstack.io/blog/rabbitmq-raspberry-pi-setup)

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Default credentials | Change `guest`/`guest` in production |
| Guest user restriction | `guest` only works from localhost by default |
| Network exposure | Use internal network (zen-garden) |
| TLS | Enable for production (ports 5671, 15671) |

**Production Setup**:
```yaml
environment:
  RABBITMQ_DEFAULT_USER: admin
  RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD}
```

## Comparison with Alternatives

| Feature | RabbitMQ | NATS | Apache Kafka |
|---------|----------|------|--------------|
| Protocol | AMQP, MQTT, STOMP | NATS | Kafka |
| Routing | Advanced (exchanges) | Simple subjects | Topics/partitions |
| Persistence | Optional | Optional | Required |
| Memory footprint | Medium | Low | High |
| ARM support | ✅ Excellent | ✅ Excellent | ⚠️ JVM overhead |
| Use case | Task queues, RPC | Pub/sub, request/reply | Event streaming |

**Recommendation for Zen Garden**:
- Task queues, RPC patterns → RabbitMQ
- Simple pub/sub, low latency → NATS
- Event sourcing, log aggregation → Kafka (if resources allow)

## Plugin System

RabbitMQ has a rich plugin ecosystem. Common plugins included in management image:

| Plugin | Port | Purpose |
|--------|------|---------|
| `rabbitmq_management` | 15672 | Web UI and HTTP API |
| `rabbitmq_prometheus` | 15692 | Prometheus metrics |
| `rabbitmq_mqtt` | 1883 | MQTT protocol |
| `rabbitmq_stomp` | 61613 | STOMP protocol |
| `rabbitmq_stream` | 5552 | RabbitMQ Streams |
| `rabbitmq_shovel` | - | Cross-cluster message moving |
| `rabbitmq_federation` | - | Cross-cluster federation |

Enable plugins via:
```bash
rabbitmq-plugins enable rabbitmq_mqtt
```

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (8 architectures)
- [x] No CPU feature requirements (AVX/SSE not needed)
- [x] Memory constraints documented (512MB minimum, 1GB recommended)
- [x] Health check command verified
- [x] Ports documented (AMQP + Management)
- [x] Security considerations reviewed
- [x] Raspberry Pi compatibility confirmed
- [x] Plugin system documented

## Files

| File | Status |
|------|--------|
| `rabbitmq.snippet.yaml` | ✅ Validated |
| `rabbitmq.compatibility.yaml` | ✅ Updated (added warning rule, healthcheck patterns) |
| `rabbitmq.frontmatter.json` | ✅ Updated (added ports array, management_port) |
| `rabbitmq.research.md` | ✅ Created |

## References

1. [RabbitMQ Official Documentation](https://www.rabbitmq.com/docs/)
2. [RabbitMQ Docker Hub](https://hub.docker.com/_/rabbitmq)
3. [RabbitMQ Monitoring Guide](https://www.rabbitmq.com/docs/monitoring)
4. [RabbitMQ Memory Threshold](https://www.rabbitmq.com/docs/memory)
5. [RabbitMQ Production Checklist](https://www.rabbitmq.com/docs/production-checklist)
6. [Erlang Version Requirements](https://www.rabbitmq.com/docs/which-erlang)
7. [RabbitMQ Sizing Guide](https://www.compilenrun.com/docs/middleware/rabbitmq/rabbitmq-best-practices/rabbitmq-sizing-guide/)
8. [RabbitMQ on Raspberry Pi](https://medium.com/swlh/how-to-build-a-rabbitmq-cluster-with-a-few-tiny-raspberry-pi-zeros-e5ffb3920e40)
