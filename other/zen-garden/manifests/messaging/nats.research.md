# NATS Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | NATS |
| **Category** | Messaging System |
| **Primary Use** | Pub/sub messaging, request/reply, streaming |
| **License** | Apache License 2.0 |
| **Governance** | Synadia/CNCF (graduated project) |
| **Project URL** | https://nats.io/ |
| **Docker Hub** | https://hub.docker.com/_/nats |
| **GitHub** | https://github.com/nats-io/nats-server |
| **Runtime** | Go binary (statically compiled) |

## Why NATS?

NATS is designed for simplicity, performance, and resilience:
- **Simple**: Minimal configuration, easy to deploy
- **Fast**: ~10 million messages/second per server
- **Lightweight**: Single binary, 10-20MB memory footprint
- **Cloud Native**: CNCF graduated project (alongside Kubernetes, Prometheus)

## Docker Image Analysis

### Image Selection
**Selected**: `nats:2.10-alpine`

Alpine variant for minimal size (~15MB compressed).

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 2.10.x | Current | JetStream GA |
| 2.9.x | Supported | Previous stable |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64v8 | ✅ | Apple Silicon, Graviton, Pi 4+ |
| arm32v7 | ✅ | Raspberry Pi 2/3 |
| arm32v6 | ✅ | Raspberry Pi Zero/1 |
| 386 | ✅ | 32-bit x86 |
| ppc64le | ✅ | IBM Power |
| s390x | ✅ | IBM Z |

**Excellent multi-architecture support** - NATS runs everywhere.

## CPU Compatibility

### No Special Requirements

NATS has **no AVX, SSE, or other SIMD requirements**.

As a Go binary, it runs on any supported architecture:
- Raspberry Pi Zero (ARMv6)
- Low-end Celeron/Atom
- Edge devices

## Resource Requirements

### Memory

NATS is exceptionally lightweight:

| Mode | Memory | Notes |
|------|--------|-------|
| Core NATS | 10-20MB | Basic pub/sub |
| JetStream | 50-100MB | With persistence |
| Heavy load | 100-500MB | High message volume |

**Formula**: ~10KB per connection + message buffers

### CPU

| Workload | Cores |
|----------|-------|
| Development | 1 |
| Small production | 1 |
| High throughput | 2-4 |

NATS is highly efficient - single core handles millions of messages.

### Disk

| Mode | Storage |
|------|---------|
| Core NATS | None (in-memory) |
| JetStream | Depends on retention |

JetStream stores messages in `/data` directory.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 4222 | TCP | Client connections |
| 6222 | TCP | Cluster routing |
| 8222 | HTTP | Monitoring |

## NATS Modes

### Core NATS (Default)
- In-memory only
- At-most-once delivery
- Pub/sub and request/reply
- Ultra-low latency

### JetStream (Enabled in snippet)
- Persistent messaging
- At-least-once delivery
- Streams with replay
- Key-value store
- Object store

**Command**: `-js` enables JetStream

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8222/healthz"]
```

**Why `/healthz`**:
- Purpose-built health endpoint
- Returns 200 when ready
- Available on monitoring port (8222)

### Monitoring Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/healthz` | Health check |
| `/varz` | Server variables |
| `/connz` | Connection info |
| `/routez` | Cluster routes |
| `/subsz` | Subscription info |
| `/jsz` | JetStream info |

## Command Line Options

| Option | Description |
|--------|-------------|
| `-js` | Enable JetStream |
| `-m 8222` | Monitoring port |
| `-p 4222` | Client port |
| `-c config.conf` | Config file |
| `-D` | Debug mode |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 64MB RAM | memory_mb_less_than | Fail | Absolute minimum |

NATS is so lightweight it runs on almost anything.

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `address already in use` | Port conflict | Change port |
| `JetStream.*error` | JetStream init failed | Check volume |
| `config.*error` | Bad configuration | Check syntax |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | ✅ | Excellent |
| Pi 4 | ✅ | Excellent |
| Pi 3 | ✅ | ARM32 supported |
| Pi 2 | ✅ | ARM32 supported |
| Pi Zero 2 W | ✅ | ARM64 |
| Pi Zero W | ✅ | ARM32v6 supported |
| Pi 1 | ✅ | ARM32v6 supported |

**NATS runs on ALL Raspberry Pi models** - exceptional edge support.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| No auth by default | Configure authentication for production |
| Network exposure | Internal network (zen-garden) |
| TLS | Configure for production |

**Production Setup**:
```conf
authorization {
  user: "user"
  password: "$NATS_PASSWORD"
}
```

Or use NKey/JWT for zero-trust authentication.

## Comparison with Alternatives

| Feature | NATS | RabbitMQ | Kafka |
|---------|------|----------|-------|
| License | Apache 2.0 | MPL 2.0 | Apache 2.0 |
| Memory footprint | ~20MB | ~256MB | ~1GB+ |
| ARM32 support | ✅ | ✅ | ❌ (JVM) |
| Setup complexity | Very low | Medium | High |
| Persistence | JetStream | Built-in | Built-in |
| Throughput | Very high | High | Very high |
| Latency | Very low | Low | Medium |
| Use case | Pub/sub, RPC | Task queues | Event streaming |

**For Zen Garden**:
- **NATS**: Lightweight pub/sub, microservices, edge
- **RabbitMQ**: Task queues, complex routing
- **Kafka**: Event sourcing, log aggregation (if resources allow)

## Client Libraries

NATS has clients for all major languages:
- Go, Rust, Python, Node.js, Java, C#, Ruby, etc.

OpenTelemetry integration available.

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (7 architectures)
- [x] No CPU feature requirements
- [x] Memory constraints documented (64MB minimum)
- [x] Health check command verified
- [x] Apache 2.0 license confirmed
- [x] CNCF graduation noted
- [x] JetStream documented

## Files

| File | Status |
|------|--------|
| `nats.snippet.yaml` | ✅ Created |
| `nats.compatibility.yaml` | ✅ Created |
| `nats.frontmatter.json` | ✅ Created |
| `nats.research.md` | ✅ Created |

## References

1. [NATS Official Documentation](https://docs.nats.io/)
2. [NATS Docker Hub](https://hub.docker.com/_/nats)
3. [NATS GitHub](https://github.com/nats-io/nats-server)
4. [JetStream Documentation](https://docs.nats.io/nats-concepts/jetstream)
5. [NATS CNCF Page](https://www.cncf.io/projects/nats/)
6. [NATS Architecture](https://docs.nats.io/nats-concepts/overview)
