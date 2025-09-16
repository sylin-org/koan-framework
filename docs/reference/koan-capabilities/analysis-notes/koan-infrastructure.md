# Koan Infrastructure Modules Analysis

## Executive Summary

Koan's infrastructure modules extend standard .NET patterns through adaptive abstractions, enterprise integration, zero-configuration defaults, production readiness, and cross-module integration that provides operational capabilities from initial deployment.

## Messaging Infrastructure Architecture

### Core Abstraction Strategy

**Three-Phase Lifecycle with Intelligent Provider Abstraction:**
- **Phase 1**: Handler Registration - Declarative message handling via fluent registration
- **Phase 2**: Provider Initialization - Auto-discovery and prioritized provider selection
- **Phase 3**: Go-Live Transition - Seamless buffer-to-live transition with message flushing

**Adaptive Message Proxy** - Zero-configuration messaging:
```csharp
// This works immediately, even before messaging provider is initialized
await new UserRegistered("u-123", "acme", "evt-1").Send();
```

**Provider Auto-Discovery with Intelligent Connection Discovery:**
- Container-first logic: Tries `rabbitmq:5672` when `Koan.Core.KoanEnv.InContainer` is true
- Environment variable cascade: `RABBITMQ_URL`, `Koan_RABBITMQ_URL`
- Localhost fallback for development scenarios
- Priority-based selection (RabbitMQ: 100, Azure Service Bus: 90, InMemory: 10)

### RabbitMQ Integration Excellence

**Enterprise-Grade Patterns:**
- **Alias-Based Routing**: Messages use aliases that map to AMQP routing keys with partition suffixes
- **Publisher Confirms**: Guarantees at-least-once delivery with configurable timeouts
- **DLQ & Retry Topology**: TTL bucket queues for scheduled delivery and retry backoff
- **Provisioning Reconciliation**: Management API integration for topology inspection

**Distribution Patterns:**
- **Round-robin**: Competing consumers share queues
- **Broadcast**: Multiple groups receive copies via fan-out
- **Topic Selection**: Wildcard routing (`*`, `#`) for selective consumption
- **Partition Sharding**: Stable hashing with ordered processing per partition

### Reliability & Ordering Guarantees

- **At-Least-Once Baseline**: Publisher confirms + consumer acknowledgments
- **Partition Ordering**: FIFO within partitions via `.p{n}` suffix routing
- **Idempotency Support**: Header-based deduplication with `[IdempotencyKey]` attributes
- **Transactional Outbox**: Future capability for cross-service reliability

## Media Processing Capabilities

### Sophisticated Media Architecture

**First-Class Media Semantics:**
```csharp
[StorageBinding("hot")]
public class VideoFile : MediaEntity<VideoFile>
{
    public string? SourceMediaId { get; set; }
    public string? RelationshipType { get; set; } // "thumbnail", "transcode"
    public string? DerivationKey { get; set; }    // Transformation identifier
}

// Rich static API
var video = await VideoFile.Upload(stream, "movie.mp4", "video/mp4");
var thumbnail = await VideoFile.EnsureDerivation(video.Id, "thumbnail-small");
```

### Media Task Processing Pipeline

**Task-Oriented Processing** - Discrete, composable media operations:
- **MediaTaskDescriptor**: Declarative task definitions with versioning
- **MediaTaskStep**: Atomic processing units with dependency management
- **MediaTaskRecord**: Execution tracking with status and result capture
- **MediaVariant**: Multiple renditions/formats for the same source

### Integration Patterns

- **Profile-Based Routing**: Media entities inherit storage profile bindings for hot/cold tiering
- **Authenticated Access**: Seamless integration with Koan.Web.Auth for protected media endpoints
- **CDN Integration**: Presigned URL support for direct client access when providers support it

## Storage Architecture & Patterns

### Multi-Provider Storage Orchestration

**Sophisticated Routing and Fallback System:**
```json
{
  "Koan": {
    "Storage": {
      "DefaultProfile": "main",
      "FallbackMode": "SingleProfileOnly",
      "Profiles": {
        "hot": { "Provider": "s3", "Container": "app-hot" },
        "cold": { "Provider": "s3", "Container": "app-archive" },
        "local": { "Provider": "local", "Container": "uploads" }
      }
    }
  }
}
```

**Advanced Capabilities:**
- **Server-Side Copy**: Optimized transfers within the same provider
- **Range Reads**: Efficient partial content delivery for large files
- **Atomic Writes**: Temp-and-rename pattern for consistency
- **Content Hashing**: SHA-256 computation during seekable uploads

### Model-Centric API Excellence

**Zero-Configuration CRUD with Type-Safe Bindings:**
```csharp
[StorageBinding("documents", "invoices")]
public class Invoice : StorageEntity<Invoice>
{
    // Inherits: CreateTextFile, ReadAllText, CopyTo<T>, MoveTo<T>
    // Plus: Head(), Exists(), Delete(), OpenRead()
}

// Fluent operations
var invoice = await Invoice.CreateTextFile("inv-123.json", jsonData);
await invoice.CopyTo<ArchivedInvoice>();
var content = await Invoice.Get("inv-123.json").ReadAllText();
```

### Distributed File Storage Patterns

- **Cross-Profile Transfers**: Orchestrated copy/move operations with automatic fallbacks
- **Presigned URLs**: Time-limited direct access for compatible providers
- **Content Delivery**: Integration points for CDN and edge caching strategies

## Security & Secret Management

### Enterprise-Grade Secret Architecture

**Provider Abstraction with Resolution Templating:**
```csharp
public interface ISecretResolver
{
    Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default);
    Task<string> ResolveAsync(string template, CancellationToken ct = default);
}
```

**Template Resolution with Embedded Secret References:**
```json
{
  "ConnectionString": "Server={{secret:database-host}};Password={{secret:db-password}}"
}
```

### HashiCorp Vault Integration

**Enterprise Patterns:**
- **Path-Based Secrets**: Hierarchical organization (`app/prod/database`, `shared/certificates`)
- **Lease Management**: Automatic renewal for dynamic secrets
- **Authentication Methods**: Token, AppRole, Kubernetes service account integration
- **Transit Encryption**: Vault as encryption-as-a-service

**Development vs Production:**
- **Dev**: In-memory provider with environment variable fallbacks
- **Production**: Vault provider with authentication and lease management
- **Kubernetes**: Service account token authentication with projected volumes

### Security Integration Patterns

- **Data Layer**: Automatic connection string resolution from secret templates
- **Web Layer**: JWT signing key rotation via secret providers
- **Messaging**: Broker credential management and rotation

## Infrastructure Services & Scheduling

### Background Job Orchestration

**Declarative Task Definition:**
```csharp
[Scheduled(FixedDelaySeconds = 30, OnStartup = true, Critical = true)]
public class HealthCheckTask : IScheduledTask, IHasTimeout, IHasMaxConcurrency
{
    public string Id => "health-check";
    public TimeSpan Timeout => TimeSpan.FromMinutes(2);
    public int MaxConcurrency => 1;

    public async Task RunAsync(CancellationToken ct)
    {
        // Bounded, monitored execution
    }
}
```

**Advanced Scheduling Features:**
- **Interface-Based Configuration**: `IOnStartup`, `IFixedDelay`, `ICronScheduled`, `IAllowedWindows`
- **Bounded Concurrency**: Per-task semaphore gates with configurable limits
- **Health Integration**: Automatic health fact publishing with TTL management
- **Graceful Shutdown**: Cancellation token propagation and timeout handling

### Redis Inbox Pattern

**Distributed Service Communication:**
- **HTTP Endpoints**: RESTful inbox operations (`GET /v1/inbox/{key}`, `POST /v1/inbox/mark-processed`)
- **Discovery Announce**: RabbitMQ-based service discovery with ping/reply pattern
- **Deduplication**: Redis-backed message deduplication with TTL windows
- **Container Integration**: Docker-native service with health checks and metrics

## Cross-Module Integration Patterns

### Message-Driven Media Workflows

**Event-Sourced Processing:**
```csharp
// Media upload triggers processing pipeline
var video = await VideoFile.Upload(stream, "content.mp4");
await new MediaProcessingRequested(video.Id, "transcode-hd").Send();

// Handler processes asynchronously
services.On<MediaProcessingRequested>(async req => {
    var task = MediaTask.Create("transcode", req.VideoId);
    await task.Execute();
    await new MediaProcessingCompleted(req.VideoId, task.ResultId).Send();
});
```

### Secret Management Integration

**Transparent Configuration Resolution:**
- **Data Layer**: Connection string templates resolved at startup
- **Storage Layer**: Provider credentials managed via secret templates
- **Messaging Layer**: Broker authentication with automatic rotation
- **Web Layer**: Certificate and key management for TLS and JWT

### Observability & Monitoring

- **Health Aggregation**: Centralized health facts from all infrastructure modules
- **Structured Logging**: Correlation IDs across message flows and background tasks
- **Metrics Collection**: Throughput, latency, error rates across storage, messaging, and scheduling
- **Distributed Tracing**: Cross-service correlation via messaging headers

## Enterprise & Production Patterns

### High Availability Architecture

**Messaging Resilience:**
- **Multi-Provider Fallback**: Automatic provider switching on failure
- **Message Buffering**: Startup-time message preservation during provider initialization
- **Dead Letter Queues**: Poison message handling with metadata preservation
- **Circuit Breaker**: Provider health monitoring with automatic failover

**Storage Redundancy:**
- **Cross-Profile Replication**: Automatic data mirroring across storage tiers
- **Disaster Recovery**: Point-in-time backup and restore capabilities via provider features
- **Geo-Distribution**: Multi-region storage with proximity-based routing

### Security & Compliance

**Enterprise Security:**
- **Secret Rotation**: Automatic credential lifecycle management
- **Audit Trails**: Comprehensive logging of secret access and storage operations
- **Encryption**: At-rest and in-transit encryption via provider capabilities
- **RBAC Integration**: Role-based access control for media and storage resources

**Compliance Features:**
- **Data Residency**: Geographic storage controls via profile configuration
- **Retention Policies**: Automated data lifecycle management
- **Access Logging**: Detailed audit trails for compliance reporting

### Multi-Tenant & Multi-Environment

**Tenant Isolation:**
- **Storage Partitioning**: Container-based tenant separation
- **Message Routing**: Tenant-aware routing keys and queue isolation
- **Secret Scoping**: Hierarchical secret organization by tenant/environment

**Environment Promotion:**
- **Configuration Inheritance**: Environment-specific overrides with safe defaults
- **Secret Management**: Environment-aware secret resolution
- **Deployment Automation**: Infrastructure-as-code patterns for consistent deployments

## Configuration & Developer Experience

### Zero-Configuration Philosophy

**Convention-Over-Configuration:**
- **Auto-Discovery**: Automatic provider detection and prioritization
- **Smart Defaults**: Production-ready defaults with development conveniences
- **Container Awareness**: Automatic container-native configuration

**Progressive Configuration:**
```csharp
// Zero config - just works
await message.Send();

// Override when needed
await message.SendTo("priority-queue");

// Full control available
builder.Services.Configure<MessagingOptions>(opts => {
    opts.Buses["rabbit"].RabbitMq.Retry.MaxAttempts = 10;
});
```

### Development-Time Experience

**Container-First Development:**
- **Docker Compose Integration**: Automatic service discovery in containerized environments
- **Hot Reload Support**: Configuration changes without container rebuilds
- **Debug Capabilities**: Rich logging and diagnostic endpoints

**Production Readiness:**
- **Health Checks**: Deep health monitoring across all infrastructure layers
- **Graceful Shutdown**: Proper resource cleanup and message flushing
- **Performance Monitoring**: Built-in metrics and profiling capabilities

### Operational Excellence

**Infrastructure Diagnostics:**
- **Provider Status**: Real-time provider health and capability reporting
- **Configuration Validation**: Startup-time configuration verification
- **Performance Metrics**: Request/response times, queue depths, error rates

**Troubleshooting Support:**
- **Detailed Logging**: Structured logs with correlation IDs and business context
- **Error Classification**: Transient vs. persistent error identification
- **Recovery Guidance**: Automated suggestions for common failure scenarios

## Module Breakdown

### Messaging Infrastructure (5 modules)
- **Koan.Messaging.Abstractions** - Messaging abstractions and patterns
- **Koan.Messaging.Core** - Core messaging infrastructure
- **Koan.Messaging.RabbitMq** - RabbitMQ messaging provider
- **Koan.Messaging.Inbox.Http** - HTTP-based inbox pattern
- **Koan.Messaging.Inbox.InMemory** - In-memory inbox for development

### Media Processing (3 modules)
- **Koan.Media.Abstractions** - Media processing abstractions
- **Koan.Media.Core** - Core media processing functionality
- **Koan.Media.Web** - Web integration for media operations

### Storage Layer (2 modules)
- **Koan.Storage** - Storage abstractions and core functionality
- **Koan.Storage.Local** - Local file system storage provider

### Security and Secrets (3 modules)
- **Koan.Secrets.Abstractions** - Secret management abstractions
- **Koan.Secrets.Core** - Core secret management functionality
- **Koan.Secrets.Vault** - HashiCorp Vault integration

### Infrastructure Services (2 modules)
- **Koan.Scheduling** - Background job scheduling and task management
- **Koan.Service.Inbox.Redis** - Redis-based service inbox pattern

## Key Differentiators

### Infrastructure Technical Features

1. **Adaptive Abstractions**: Smart proxies and providers that reduce startup complexity
2. **Enterprise Integration**: Native support for HashiCorp Vault, RabbitMQ topology management, and multi-provider storage
3. **Developer Experience**: Zero-configuration defaults with progressive disclosure of advanced features
4. **Production Readiness**: Built-in observability, health monitoring, and operational patterns
5. **Cross-Module Integration**: Consistent integration patterns between messaging, storage, secrets, and scheduling

## Conclusion

This infrastructure foundation provides enterprise applications with **operational capabilities from initial deployment** while maintaining the simplicity and productivity that makes Koan useful for development teams. The architecture implements distributed systems patterns with practical solutions that address enterprise requirements around reliability, security, observability, and operational complexity - making Koan suitable for production applications that need both developer productivity and enterprise operational capabilities.