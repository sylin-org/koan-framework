---
id: SERV-0001
slug: koan-services-architecture
domain: Services
status: Accepted
date: 2025-10-29
title: Koan Services Architecture - Composable Capability Services
---

# SERV-0001: Koan Services Architecture

Date: 2025-10-29

Status: Accepted

## Context

Koan Framework provides strong abstractions for data (Entity, Vector) and AI (IAi). However, complex application capabilities like translation, OCR, speech-to-text, and image generation require integration of multiple subsystems and lack first-class framework support. Developers must manually wire infrastructure, handle service discovery, implement load balancing, and expose HTTP APIs for non-.NET consumers.

Current state problems:
- No standard pattern for adding application-level services
- Manual infrastructure setup (HTTP clients, service discovery, health checks)
- Inconsistent APIs across different capabilities
- No unified approach for dual deployment (in-process vs. containerized)
- Poor non-Koan consumer experience (no standard HTTP interface)

## Decision

Introduce **Koan Services** as a first-class framework capability extending "Reference = Intent" to full application services.

### Core Architectural Decisions

**1. Entity-Pattern API Surface**

Services follow the same static method conventions as Entity and Vector:

```csharp
// Entity pattern (existing)
var todo = await Todo.Get(id);

// Vector pattern (existing)
var results = await Vector<Media>.Search(embedding);

// Service pattern (NEW)
var result = await Translation.Translate(text, targetLang);
```

Key conventions:
- Static methods (no interfaces required for consumers)
- No "Async" suffix (everything is async by default)
- Simple, direct method names
- CancellationToken last parameter with default value

**2. Dual Deployment Modes**

Every service available as:
- **NuGet Package** (in-process): Fast development, zero network latency
- **Docker Container** (remote): Independent scaling, technology-agnostic

Same application code works in both modes transparently.

**3. Three-Tier Communication Architecture**

Services communicate via three distinct channels:

**Tier 1: Orchestrator Channel** (239.255.42.1:42001) - Discovery & Control Plane
- **Global channel** - ALL services and framework join
- Discovery requests and responses
- Service announcements and heartbeats
- Cross-cutting orchestration messages
- Mandatory for all services

**Tier 2: Service-Specific Channels** (Optional) - Service Dialog Plane
- Per-service multicast groups (e.g., Translation: 239.255.42.10:42010)
- Service-to-service pub/sub communication
- Broadcast notifications to all instances of a service
- Event streaming within service boundaries
- Optional - only if service needs it

**Tier 3: HTTP Endpoints** - Request/Response Data Plane
- Per-instance HTTP endpoints (e.g., http://172.18.0.3:8080)
- Actual service invocations
- Request/response patterns
- RESTful APIs for external consumers
- Mandatory for all services

**Communication Flow**:
```
Orchestrator Channel: 239.255.42.1:42001 (UDP, mandatory, global)
├─ Discovery: Who provides translation?
├─ Announcements: Translation available at http://172.18.0.3:8080
└─ Heartbeats: Every 30s

Translation Service Channel: 239.255.42.10:42010 (UDP, optional, service-specific)
├─ Translation #1 listens
├─ Translation #2 listens
├─ Translation #3 listens
└─ Pub/Sub: Cache invalidation, config updates, etc.

HTTP Endpoints: http://172.18.0.3:8080 (TCP, mandatory, per-instance)
├─ POST /api/translate → actual translation work
├─ GET /health → health check
└─ GET /.well-known/koan-service → manifest
```

**Use Cases**:
- **Orchestrator Channel**: "Who can translate?" "I can, here's my endpoint"
- **Service Channel**: "All translation instances: clear cache for language=es"
- **HTTP Endpoint**: "Translate 'hello' to Spanish" → "hola"

**4. Automatic Load Balancing**

Framework tracks multiple service instances:
- Round-robin distribution by default
- Least-connections and health-aware policies available
- Stale instance removal (no heartbeat > 2 minutes = removed)
- Per-service instance health tracking

**5. Universal HTTP Interface**

All services expose standard REST API for non-Koan consumers:
- `/.well-known/koan-service` - Service manifest (RFC 8615)
- `/health` - Health check endpoint
- `/api/{capability}` - Capability execution endpoints

**6. Attribute-Driven Configuration**

Services use declarative attributes matching existing Koan patterns:

```csharp
// Matches existing patterns
[DataAdapter("mongodb")]        // Data layer
[VectorAdapter("weaviate")]     // Vector layer
[KoanService("translation")]    // Service layer ✅

// Minimal declaration - everything auto-configured
[KoanService("translation")]
public class TranslationService
{
    // Port, multicast group, heartbeat interval use defaults
    // Capabilities auto-detected from public methods
    // Announces to mesh automatically in containers
}
```

Configuration hierarchy (lower priority → higher priority):
1. Attribute defaults
2. Attribute explicit values
3. appsettings.json
4. Environment variables
5. Code overrides

**7. Zero Configuration by Default**

```csharp
// Service side
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
// ✅ Scans for [KoanService] attributes
// ✅ Announces to mesh automatically
// ✅ Exposes endpoints automatically

// Consumer side
builder.Services.AddKoan();
// ✅ Discovers services automatically
await Translation.Translate("Hello", "es");
```

## Specification

### Service Infrastructure Components

**Koan.Services.Abstractions**
- `IKoanServiceMesh` - Service discovery and announcement abstraction
- `ServiceInstance` - Instance metadata (ID, endpoint, health, capabilities)
- `ServiceExecutor<T>` - Routing logic (in-process vs. remote execution)
- Attribute markers: `[KoanService]`, `[KoanCapability]`

**Koan.Services** (Core Implementation)
- `KoanServiceMesh` - UDP multicast implementation (internal)
- `KoanServiceMeshCoordinator` - Background service for mesh maintenance
- `ServiceContext` - Static service locator for consumer APIs
- `ServiceRegistry` - Instance tracking with health monitoring

**Package Structure**:
```
Koan.Services/
├── Abstractions/           # Public contracts
│   ├── KoanServiceAttribute.cs
│   ├── KoanCapabilityAttribute.cs
│   ├── IKoanServiceMesh.cs
│   └── ServiceInstance.cs
├── Discovery/
│   └── KoanServiceMesh.cs  # UDP/multicast (internal)
├── Execution/
│   └── ServiceExecutor.cs  # Routing logic (internal)
└── Coordination/
    └── KoanServiceMeshCoordinator.cs  # Background service
```

### Service Attribute Specification

**KoanServiceAttribute** - Declarative service definition:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class KoanServiceAttribute : Attribute
{
    // Identity (required)
    public string ServiceId { get; set; }

    // Display
    public string? DisplayName { get; set; }        // Default: ServiceId with spaces
    public string? Description { get; set; }

    // HTTP endpoint configuration (overridable via config)
    public int Port { get; set; } = 8080;
    public string HealthEndpoint { get; set; } = "/health";
    public string ManifestEndpoint { get; set; } = "/.well-known/koan-service";

    // Orchestrator channel (global, mandatory, rarely overridden)
    public string OrchestratorMulticastGroup { get; set; } = "239.255.42.1";
    public int OrchestratorMulticastPort { get; set; } = 42001;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int StaleThresholdSeconds { get; set; } = 120;

    // Service-specific channel (optional, for service dialog)
    public string? ServiceMulticastGroup { get; set; } = null;  // e.g., "239.255.42.10"
    public int? ServiceMulticastPort { get; set; } = null;      // e.g., 42010
    public bool EnableServiceChannel { get; set; } = false;     // Opt-in

    // Capability detection
    public string[]? Capabilities { get; set; }     // Null = auto-detect from methods

    // Deployment hints
    public string? ContainerImage { get; set; }     // e.g., "koan/service-translation"
    public string? DefaultTag { get; set; } = "latest";

    public KoanServiceAttribute(string serviceId)
    {
        ServiceId = serviceId;
    }
}
```

**Three-Tier Configuration**:
- **Tier 1 (Orchestrator)**: `OrchestratorMulticastGroup` (239.255.42.1) + `OrchestratorMulticastPort` (42001) - Global, mandatory
- **Tier 2 (Service-Specific)**: `ServiceMulticastGroup` + `ServiceMulticastPort` + `EnableServiceChannel` - Optional, per-service
- **Tier 3 (HTTP)**: `Port` (8080) - Mandatory, per-instance

**Usage Examples**:

Minimal (orchestrator + HTTP only):
```csharp
[KoanService("translation")]
public class TranslationService
{
    // Tier 1 - Orchestrator: 239.255.42.1:42001 (global discovery)
    // Tier 2 - Service Channel: disabled (no service-specific dialog)
    // Tier 3 - HTTP Port: 8080 (service invocations)
    // HeartbeatInterval: 30s
    // Capabilities: auto-detected from methods
}
```

With service-specific channel (for advanced scenarios):
```csharp
[KoanService(
    serviceId: "translation",
    DisplayName = "AI Translation Service",
    Description = "Multi-provider translation supporting 200+ languages",
    Port = 9090,  // Custom HTTP port
    EnableServiceChannel = true,
    ServiceMulticastGroup = "239.255.42.10",
    ServiceMulticastPort = 42010,
    Capabilities = new[] { "translate", "detect-language", "list-languages" },
    ContainerImage = "koan/service-translation",
    HeartbeatIntervalSeconds = 15
)]
public class TranslationService
{
    private readonly IServiceChannelPublisher _channelPublisher;

    // Can broadcast to all translation instances
    public async Task InvalidateCacheAsync(string language)
    {
        await _channelPublisher.BroadcastAsync(new CacheInvalidationMessage
        {
            Language = language,
            Timestamp = DateTime.UtcNow
        });
        // All translation instances receive this on 239.255.42.10:42010
    }
}
// Uses: Orchestrator (239.255.42.1:42001) + Service Channel (239.255.42.10:42010) + HTTP (9090)
```

Override via configuration:
```json
{
  "Koan": {
    "Services": {
      "Translation": {
        "Port": 8081,
        "HeartbeatIntervalSeconds": 60,
        "EnableServiceChannel": true,
        "ServiceMulticastGroup": "239.255.42.10",
        "ServiceMulticastPort": 42010
      },
      "Orchestrator": {
        "MulticastGroup": "239.255.42.100",
        "MulticastPort": 42002
      }
    }
  }
}
```

Override via environment variables:
```bash
# Per-service configuration
KOAN__SERVICES__TRANSLATION__PORT=8082
KOAN__SERVICES__TRANSLATION__HEARTBEATINTERVALSECONDS=45
KOAN__SERVICES__TRANSLATION__ENABLESERVICECHANNEL=true
KOAN__SERVICES__TRANSLATION__SERVICEMULTICASTGROUP=239.255.42.10
KOAN__SERVICES__TRANSLATION__SERVICEMULTICASTPORT=42010

# Global orchestrator channel (affects all services)
KOAN__SERVICES__ORCHESTRATOR__MULTICASTGROUP=239.255.42.100
KOAN__SERVICES__ORCHESTRATOR__MULTICASTPORT=42002
```

**When to Use Service-Specific Channels**:
- ✅ **Cache invalidation** across all service instances
- ✅ **Configuration updates** broadcasted to all instances
- ✅ **Coordinated actions** requiring all instances to respond
- ✅ **Event streaming** within service boundaries
- ❌ **NOT for request/response** - use HTTP endpoints instead
- ❌ **NOT for discovery** - use orchestrator channel instead

Most services don't need service-specific channels (use HTTP + orchestrator only).

### Service Announcement Protocol

**Discovery happens on the orchestrator channel (239.255.42.1:42001)**

Optional service-specific channels (e.g., 239.255.42.10:42010) are for pub/sub within service boundaries, not discovery.

**Announcement Message** (Service → Orchestrator Channel, JSON over UDP):
```json
{
  "type": "service-available",
  "serviceId": "translation",
  "instanceId": "d4f2a1b3",
  "httpEndpoint": "http://172.18.0.3:8080",
  "capabilities": ["translate", "detect-language"],
  "timestamp": "2025-10-29T10:30:00Z"
}
```
Sent on service startup and every 30 seconds (heartbeat).

**Discovery Request** (Orchestrator → Services, JSON over UDP):
```json
{
  "type": "discover-services",
  "serviceId": null,  // null = all services
  "requestId": "req-abc123"
}
```
Broadcast by orchestrator on startup or when refreshing service list.

**Discovery Response**: Services respond with announcement message

**Heartbeat**: Services send announcement message every 30 seconds to orchestrator channel

**Stale Detection**: Orchestrator removes instance if no heartbeat received for 2 minutes

**Protocol Flow**:
```
1. App starts → joins orchestrator channel (239.255.42.1:42001)
2. App sends "discover-services" broadcast
3. Services (already listening) respond with "service-available"
4. Services continue sending heartbeats every 30s
5. App tracks all services, removes stale instances
```

### Service Definition Pattern

**Implementation Class** (service-side) with attribute-driven configuration:

```csharp
// Koan.Services.Translation/TranslationService.cs
[KoanService("translation")]
public class TranslationService
{
    private readonly IAi _ai;

    public TranslationService(IAi ai) => _ai = ai;

    // Capability auto-detected as "translate" from method name
    public async Task<TranslationResult> Translate(
        TranslationOptions options,
        CancellationToken ct = default)
    {
        var prompt = BuildTranslationPrompt(options);
        var result = await _ai.PromptAsync(prompt, ct: ct);

        return new TranslationResult
        {
            TranslatedText = result,
            DetectedSourceLang = options.SourceLang,
            // ...
        };
    }

    // Auto-detected as "detect-language" capability
    public async Task<LanguageDetectionResult> DetectLanguage(
        string text,
        CancellationToken ct = default)
    {
        // Implementation
    }
}
```

**Capability Auto-Detection**:

Framework scans public methods returning `Task<T>`:
- Method name converted to kebab-case: `Translate` → `"translate"`, `DetectLanguage` → `"detect-language"`
- Can be overridden with `[KoanCapability("custom-name")]` attribute
- Can be specified explicitly in `[KoanService(Capabilities = new[] {...})]`

**Static Service Class** (consumer-facing):

```csharp
// Koan.Services.Translation/Translation.cs
public static class Translation
{
    public static Task<TranslationResult> Translate(
        string text,
        string targetLang,
        CancellationToken ct = default)
    {
        return Translate(new TranslationOptions
        {
            Text = text,
            SourceLang = "auto",
            TargetLang = targetLang
        }, ct);
    }

    public static async Task<TranslationResult> Translate(
        TranslationOptions options,
        CancellationToken ct = default)
    {
        var executor = ServiceContext.Get<Translation>();
        return await executor.Execute(options, ct);
    }

    public static async Task<LanguageDetectionResult> DetectLanguage(
        string text,
        CancellationToken ct = default)
    {
        var executor = ServiceContext.Get<Translation>();
        return await executor.Execute(new { Text = text }, ct);
    }
}
```

**Framework Auto-Registration** (internal):

```csharp
// Koan.Services/Initialization/ServicesAutoRegistrar.cs
public class ServicesAutoRegistrar : IKoanInitializer
{
    public void Initialize(IServiceCollection services, IConfiguration config)
    {
        // Scan assemblies for [KoanService] attributes
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<KoanServiceAttribute>();
                if (attr == null) continue;

                // Auto-register service instance
                services.AddSingleton(type);

                // Build descriptor with configuration hierarchy
                var descriptor = BuildServiceDescriptor(type, attr, config);
                services.AddSingleton(descriptor);

                Logger.Information(
                    "Koan:services {ServiceId}→registered (port: {Port}, capabilities: {Capabilities})",
                    attr.ServiceId,
                    descriptor.Port,
                    string.Join(",", descriptor.Capabilities)
                );
            }
        }

        // Initialize service mesh if in container
        if (KoanEnv.InContainer)
        {
            services.AddSingleton<IKoanServiceMesh, KoanServiceMesh>();
            services.AddHostedService<KoanServiceMeshCoordinator>();
        }
    }

    private KoanServiceDescriptor BuildServiceDescriptor(
        Type serviceType,
        KoanServiceAttribute attr,
        IConfiguration config)
    {
        var sectionPath = $"Koan:Services:{attr.ServiceId}";
        var section = config.GetSection(sectionPath);

        // Orchestrator channel is global (same for all services)
        var orchestratorSection = config.GetSection("Koan:Services:Orchestrator");

        return new KoanServiceDescriptor
        {
            ServiceId = attr.ServiceId,
            DisplayName = attr.DisplayName ?? ToDisplayName(attr.ServiceId),
            Description = attr.Description,
            ServiceType = serviceType,

            // Per-service configuration hierarchy: attribute → appsettings → env vars
            Port = section.GetValue("Port", attr.Port),
            HeartbeatInterval = TimeSpan.FromSeconds(
                section.GetValue("HeartbeatIntervalSeconds", attr.HeartbeatIntervalSeconds)
            ),

            // Global orchestrator channel (rarely overridden)
            OrchestratorMulticastGroup = orchestratorSection.GetValue(
                "MulticastGroup",
                attr.OrchestratorMulticastGroup
            ),
            OrchestratorMulticastPort = orchestratorSection.GetValue(
                "MulticastPort",
                attr.OrchestratorMulticastPort
            ),

            // Auto-detect capabilities if not specified
            Capabilities = attr.Capabilities ?? DetectCapabilities(serviceType),

            ContainerImage = attr.ContainerImage,
            DefaultTag = attr.DefaultTag
        };
    }

    private string[] DetectCapabilities(Type serviceType)
    {
        // Auto-detect from public methods returning Task<T>
        return serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.ReturnType.IsGenericType &&
                        m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            .Select(m => ToKebabCase(m.Name))
            .Distinct()
            .ToArray();
    }

    private string ToKebabCase(string name)
    {
        // "DetectLanguage" → "detect-language"
        return Regex.Replace(name, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
    }
}
```

**Boot Report**:
```
[INFO] Koan:services translation→registered (http: :8080, capabilities: translate,detect-language)
[INFO] Koan:services ocr→registered (http: :8080, capabilities: extract-text)
[INFO] Koan:mesh initialized (orchestrator: 239.255.42.1:42001)
[INFO] Koan:mesh listening for service announcements
```

All services share the same orchestrator channel (239.255.42.1:42001). Each service has its own HTTP port for actual service invocation.

### Container Service Implementation

**Program.cs** (complete service):
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();  // All infrastructure automatic

var app = builder.Build();
await app.RunAsync();
```

Behind the scenes (framework code):
- Scans for `[KoanService]` attributes during `AddKoan()`
- Registers service in DI container automatically
- `KoanServiceMeshCoordinator` starts as hosted service
- Service joins **global orchestrator channel** (239.255.42.1:42001)
- Service announces to orchestrator channel with HTTP endpoint (e.g., http://172.18.0.3:8080)
- Service sends heartbeats every 30s to orchestrator channel
- Standard HTTP endpoints generated from attribute metadata:
  - `/.well-known/koan-service` → manifest from attribute
  - `/health` → health check
  - `/api/translate` → routes to `Translate()` method
  - `/api/detect-language` → routes to `DetectLanguage()` method
- Capabilities auto-detected from public `Task<T>` methods

**Discovery Architecture**:
```
Orchestrator Channel: 239.255.42.1:42001 (UDP multicast)
├─ Framework App joins channel, broadcasts "discover-services"
├─ Translation Service (container 1) announces: http://172.18.0.3:8080
├─ Translation Service (container 2) announces: http://172.18.0.4:8080
├─ OCR Service announces: http://172.18.0.5:8080
└─ All communicate via this single channel
```

Service invocations happen via HTTP to announced endpoints, NOT via multicast.

**Dockerfile**:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY publish/ .

LABEL koan.service.id="translation"
LABEL koan.service.capabilities="translate,detect-language"

ENTRYPOINT ["dotnet", "Koan.Services.Translation.dll"]
```

### Non-Koan Consumer Support

**Service Manifest** (`/.well-known/koan-service`):
```json
{
  "serviceId": "translation",
  "version": "1.0.0",
  "displayName": "AI Translation Service",
  "capabilities": [
    {
      "name": "translate",
      "endpoint": "/api/translate",
      "method": "POST",
      "inputSchema": {
        "type": "object",
        "properties": {
          "text": {"type": "string"},
          "targetLang": {"type": "string"}
        }
      }
    }
  ]
}
```

**Usage Examples**:

Python:
```python
import requests

manifest = requests.get("http://translation:8080/.well-known/koan-service").json()
print(f"Service: {manifest['displayName']}")

result = requests.post("http://translation:8080/api/translate",
    json={"text": "Hello", "targetLang": "es"}).json()
print(result["translatedText"])
```

JavaScript:
```javascript
const result = await fetch('http://translation:8080/api/translate', {
  method: 'POST',
  body: JSON.stringify({text: 'Hello', targetLang: 'es'})
}).then(r => r.json());

console.log(result.translatedText);
```

curl:
```bash
curl -X POST http://translation:8080/api/translate \
  -H "Content-Type: application/json" \
  -d '{"text":"Hello","targetLang":"es"}'
```

## Translation Service Reference Implementation

### Package Structure

```
Koan.Services.Translation/
├── Abstractions/
│   ├── TranslationOptions.cs
│   ├── TranslationResult.cs
│   └── LanguageDetectionResult.cs
├── Implementation/
│   ├── TranslationService.cs
│   ├── Translation.cs              # Static consumer API
│   ├── Providers/
│   │   ├── OllamaTranslationProvider.cs
│   │   ├── GoogleTranslateProvider.cs
│   │   └── LibreTranslateProvider.cs
│   └── Initialization/
│       └── KoanAutoRegistrar.cs
└── Container/
    ├── Program.cs
    ├── Dockerfile
    └── docker-compose.service.yml
```

### Key Features

**Multi-Provider Support**:
- Ollama (Qwen2.5) - Default, local, free
- Google Translate - Requires API key, 200+ languages
- LibreTranslate - Open-source alternative

**Auto-Detection**:
- Source language detection via "auto" parameter
- Provider selection based on availability and configuration

**Cost Tracking**:
```csharp
public record TranslationResult
{
    public string TranslatedText { get; init; } = "";
    public string DetectedSourceLang { get; init; } = "";
    public string Provider { get; init; } = "";
    public decimal Cost { get; init; }
    public string CostCurrency { get; init; } = "USD";
}
```

**Usage**:
```csharp
// Simple
var result = await Translation.Translate("Hello world", "es");

// With options
var result = await Translation.Translate(new TranslationOptions
{
    Text = "Hello world",
    SourceLang = "en",
    TargetLang = "fr",
    PreserveFormatting = true
});

// Language detection
var detected = await Translation.DetectLanguage("Bonjour");
```

## Consequences

### Positive

**Consistency with Framework Patterns**:
- Service APIs match Entity/Vector static method conventions
- "Reference = Intent" extends to application capabilities
- Provider transparency applies at service level (multi-provider support)

**Developer Experience**:
- Zero-config by default (services discover each other)
- Same code works in-process or containerized
- Gradual adoption path (start in-process, scale to containers)
- Simple debugging (in-process) and production deployment (containers)

**Three-Tier Communication Architecture**:
- Orchestrator channel provides reliable discovery without registry infrastructure
- Optional service channels enable pub/sub patterns for advanced scenarios
- HTTP endpoints provide universal access for any language/tool
- Right protocol for each communication pattern (UDP for broadcast, HTTP for request/response)

**Universal Access**:
- Standard HTTP REST API for any language
- Well-known manifest endpoint for discoverability
- No Koan-specific tooling required for non-.NET consumers

**Production-Ready Infrastructure**:
- Automatic load balancing across instances
- Health monitoring and failover
- No single point of failure (peer-to-peer announcement)
- Container-native design

**Framework Extension**:
- Clear pattern for community service development
- Reusable infrastructure for all services
- Ecosystem emergence (third-party services possible)

### Negative

**Network Dependency**:
- UDP multicast requires proper network configuration
- May not work in all container orchestration environments
- Fallback to explicit configuration needed for restrictive networks

**Operational Complexity**:
- Multiple deployment modes to maintain (NuGet + Docker)
- Service mesh coordination adds background processing
- Debugging distributed scenarios more complex than monolithic

**Infrastructure Overhead**:
- UDP multicast background service consumes resources
- Service registry memory overhead for instance tracking
- HTTP serialization overhead for remote calls

### Risks and Mitigations

**Risk**: UDP multicast blocked in production networks
- **Mitigation**: Explicit service endpoint configuration via appsettings.json
- **Fallback**: Environment variables for service URLs

**Risk**: Service version incompatibility
- **Mitigation**: Version included in service manifest
- **Future**: Semantic versioning checks in framework

**Risk**: Network partitions causing split-brain
- **Mitigation**: Instance IDs prevent duplicate registration
- **Future**: Distributed consensus for critical services

## Rationale

### Why Attribute-Driven Configuration

**Attribute-Driven Advantages**:
- Consistency with existing Koan patterns (`[DataAdapter]`, `[VectorAdapter]`)
- Zero configuration by default (sensible defaults in attribute)
- Self-documenting code (all service metadata visible at class level)
- Progressive configuration (start minimal, add details as needed)
- Hierarchical overrides (attribute → appsettings → env vars → code)
- Framework control (infrastructure hidden from users)

**Existing Koan Pattern Consistency**:
```csharp
[DataAdapter("mongodb")]        // Data layer
[VectorAdapter("weaviate")]     // Vector layer
[KoanService("translation")]    // Service layer ✅
```

All three follow the same declarative approach: attributes define routing and configuration, framework handles infrastructure.

**Configuration Hierarchy**:
1. **Attribute defaults** - Sensible out-of-box behavior
2. **Attribute explicit values** - Developer intent in code
3. **appsettings.json** - Environment-specific overrides
4. **Environment variables** - Container/deployment overrides
5. **Code overrides** - Runtime dynamic configuration

This matches Koan's existing configuration resolution pattern used throughout the framework.

**Auto-Discovery Benefits**:
- Framework scans for `[KoanService]` during startup
- No manual registration needed (`IKoanInitializer` pattern)
- Capabilities auto-detected from methods
- Endpoints auto-generated from metadata
- Boot reports show what was discovered

**Alternatives Considered**:
- **Configuration-only** (appsettings.json): Separates definition from implementation, harder to discover services
- **Manual registration** (services.AddService<T>()): Boilerplate, doesn't match "Reference = Intent"
- **Convention-based** (class naming): Fragile, magic strings, no explicit intent

**Decision**: Attribute-driven configuration provides the right balance of explicitness, flexibility, and Koan pattern consistency.

### Why Three-Tier Communication Architecture

**Design Philosophy**: Different communication patterns require different protocols.

**Tier 1: Orchestrator Channel (UDP Multicast)**

Why UDP multicast for discovery:
- **Single well-known channel** (239.255.42.1:42001) - all services join same group
- **Zero configuration** - no central registry or service mesh infrastructure needed
- **Peer-to-peer** - no single point of failure, orchestrator can restart without losing service state
- **Low latency discovery** - milliseconds vs. seconds for HTTP-based discovery
- **Works in Docker Compose** without additional services (Consul, etcd, etc.)
- **Framework/Orchestrator knows where to listen** - no need to guess service-specific channels
- **Services don't need to know each other** - only the orchestrator channel

**Tier 2: Service-Specific Channels (UDP Multicast, Optional)**

Why optional per-service channels:
- **Pub/sub within service boundaries** - broadcast to all instances of a service
- **Cache invalidation** - coordinate state across replicas
- **Configuration updates** - push config changes to all instances
- **Event streaming** - service-internal events
- **Opt-in** - only services that need this enable it (YAGNI principle)
- **Isolated** - translation channel doesn't interfere with OCR channel

**Tier 3: HTTP Endpoints (TCP)**

Why HTTP for invocations:
- **Request/response** - natural fit for HTTP
- **Universal** - any language/tool can call HTTP
- **RESTful** - standard patterns, well-understood
- **Load balancing** - framework routes to discovered endpoints
- **Stateless** - each request independent

**Three-Tier Architecture**:
```
1 Orchestrator Channel (UDP, mandatory, global)
├─ Discovery: orchestrator ↔ all services
└─ Cross-cutting coordination

N Service Channels (UDP, optional, per-service-type)
├─ Service-internal pub/sub
└─ Coordinate replicas (cache, config, events)

M HTTP Endpoints (TCP, mandatory, per-instance)
├─ Actual service invocations
└─ Request/response patterns
```

**Alternatives Considered**:
- **Single HTTP-based discovery**: Inefficient polling, requires hardcoded endpoints
- **DNS-SD/Bonjour**: Requires additional daemon, not universally supported in containers
- **Consul/etcd**: Too heavyweight for simple scenarios, requires deployment and management
- **Redis Pub/Sub for discovery**: Requires Redis dependency, introduces coupling
- **Only orchestrator channel, no service channels**: Would force HTTP for pub/sub (inefficient)
- **Only service channels, no orchestrator**: Orchestrator can't discover without knowing channels upfront ❌

**Decision**: Three-tier architecture provides optimal protocol for each communication pattern. Orchestrator channel for discovery (mandatory), service channels for pub/sub (optional), HTTP for invocations (mandatory).

### Why Static Classes vs. Interfaces

**Static Class Advantages**:
- Matches existing Entity/Vector patterns
- No DI injection required in consumers
- Simpler API surface (no interface ceremony)
- Type inference works naturally

**Alternatives Considered**:
- `ITranslationService` interface: More ceremony, doesn't match Koan patterns
- Fluent builder (`KoanServices.Do<T>()`): Over-engineering, verbose

**Decision**: Static classes align with Koan's entity-first philosophy.

### Why Dual Deployment vs. Container-Only

**Dual Deployment Advantages**:
- Fast inner-loop development (in-process, no container rebuild)
- Easy debugging (same process, F5 works)
- Gradual adoption (start simple, scale to containers)
- Matches existing Koan adapter pattern

**Alternatives Considered**:
- Container-only: Slower development cycle, debugging harder
- NuGet-only: No independent scaling, language lock-in

**Decision**: Dual deployment provides best of both worlds.

## Adoption Plan

### Phase 1: Core Infrastructure (Week 1-2)

1. **Create base packages**:
   - `Koan.Services.Abstractions` - Public contracts
     - `KoanServiceAttribute` with full configuration options
     - `KoanCapabilityAttribute` for explicit capability naming
     - `IKoanServiceMesh` interface
     - `ServiceInstance` and `KoanServiceDescriptor` models
   - `Koan.Services` - Core implementation (internal)
     - UDP multicast service mesh
     - Service executor with routing logic
     - Attribute scanning and auto-registration

2. **Framework integration**:
   - Implement `ServicesAutoRegistrar : IKoanInitializer`
   - Attribute scanning logic with capability auto-detection
   - Configuration hierarchy resolver (attribute → config → env vars)
   - Extend `services.AddKoan()` to scan for `[KoanService]` attributes
   - Initialize service mesh if `KoanEnv.InContainer`
   - Add `ServiceContext` for static service access
   - Create `KoanServiceMeshCoordinator` hosted service

3. **Testing**:
   - Unit tests for attribute scanning
   - Unit tests for configuration hierarchy
   - Unit tests for capability auto-detection
   - Integration tests for UDP discovery
   - Multi-instance load balancing tests

### Phase 2: Translation Service (Week 3-4)

1. **Implement Translation service**:
   - `TranslationService` with `[KoanService("translation")]` attribute
   - Ollama, Google Translate, LibreTranslate providers
   - `Translation` static class (consumer API)
   - Capability auto-detection from public methods
   - No manual registration needed (attribute-driven)

2. **Container packaging**:
   - Dockerfile and docker-compose.service.yml
   - Auto-announcement on startup
   - Standard endpoint exposure

3. **Sample application**:
   - `S8.Polyglot` sample showing both deployment modes
   - Multi-language examples (C#, Python, JavaScript, curl)
   - Load balancing demonstration

### Phase 3: Additional Services (Week 5-8)

1. **OCR Service** (Tesseract):
   - Image → text extraction
   - Multi-language OCR support
   - Service composition example (OCR + Translation)

2. **Speech-to-Text Service** (Whisper):
   - Audio → text transcription
   - Multi-language support
   - Streaming audio support

3. **Community tooling**:
   - Service template generator
   - Client SDK generator (from manifest)
   - Service catalog documentation

### Phase 4: Production Hardening (Week 9-12)

1. **Additional discovery mechanisms**:
   - Kubernetes service discovery adapter
   - Consul adapter
   - Configuration-based fallback

2. **Observability**:
   - Service mesh health dashboard
   - Request tracing across services
   - Cost aggregation and reporting

3. **Documentation**:
   - Architecture guide
   - Service development guide
   - Deployment patterns
   - Troubleshooting guide

## Alternatives Considered

### Alternative 1: MCP (Model Context Protocol)

**Description**: Use Anthropic's MCP for service discovery and communication

**Pros**:
- Standard protocol emerging in AI space
- JSON-RPC based, well-defined
- Tool discovery built-in

**Cons**:
- AI-specific, not general-purpose
- Requires MCP runtime/server
- More complex than needed for internal services
- Doesn't solve deployment flexibility

**Verdict**: MCP is complementary for AI tool integration, not service mesh

### Alternative 2: gRPC

**Description**: Use gRPC for service communication

**Pros**:
- Efficient binary protocol
- Built-in service discovery via reflection
- Strong typing

**Cons**:
- HTTP/2 requirement (not all environments)
- Complex debugging (binary protocol)
- Tooling gaps for non-.NET
- Doesn't match Koan's REST/JSON conventions

**Verdict**: HTTP+JSON is simpler and more universal

### Alternative 3: OpenAPI + Code Generation

**Description**: Define services via OpenAPI specs, generate clients

**Pros**:
- Standard, tool-rich ecosystem
- Multi-language client generation
- Contract-first development

**Cons**:
- Requires manual spec maintenance
- Build-time code generation complexity
- Doesn't solve discovery problem
- More ceremony than Koan patterns

**Verdict**: OpenAPI can complement (optional), not replace framework patterns

### Alternative 4: Azure Service Bus / RabbitMQ

**Description**: Use message broker for service communication

**Pros**:
- Proven at scale
- Built-in reliability
- Publish-subscribe patterns

**Cons**:
- Requires broker deployment/management
- Over-engineering for simple RPC
- Latency overhead
- Conceptual mismatch (async messaging vs. sync RPC)

**Verdict**: Message brokers for event-driven patterns, not service mesh

## Follow-ups

### Immediate (Phase 1)

- [ ] Create `Koan.Services.Abstractions` package
- [ ] Implement UDP multicast service mesh
- [ ] Add `KoanServiceMeshCoordinator` hosted service
- [ ] Write discovery integration tests

### Short-term (Phase 2)

- [ ] Implement Translation service
- [ ] Create Translation container
- [ ] Build S8.Polyglot sample
- [ ] Document service development patterns

### Medium-term (Phase 3-4)

- [ ] Add Kubernetes discovery adapter
- [ ] Create service catalog UI
- [ ] Implement distributed tracing
- [ ] Add cost aggregation features

### Long-term (Future)

- [ ] Service versioning and compatibility checks
- [ ] Circuit breaker and retry policies
- [ ] Service composition patterns
- [ ] Third-party service marketplace

## References

- ARCH-0049: Unified service metadata and discovery
- ARCH-0054: Framework positioning as container-native
- DATA-0054: Vector Search capability and contracts (parallel capability pattern)
- AI-0009: Multi-service routing and policies (AI adapter pattern inspiration)

---

**Author**: Framework Architecture Team
**Reviewers**: Enterprise Architecture, Developer Experience
**Implementation Status**: Phase 1 In Progress
