# S8.Location: Canonical Location Standardization System

**Project Status**: Implementation Ready  
**Architecture**: CLD Orchestrator Bidirectional Pattern with SHA512 Deduplication  
**Framework**: Sora Framework with Flow, Data, Messaging, and AI integration

---

## Overview

S8.Location is a canonical location storage system that deduplicates addresses from multiple source systems using AI-powered resolution and geocoding. It implements the **Orchestrator Bidirectional Pattern** with SHA512-based caching to achieve 95%+ cache hit rates and dramatic cost reduction for location resolution.

### Core Problem Solved

Multiple source systems provide addresses in different formats that represent the same physical location:

```
Source A (Inventory):     "96 1st street Middle-of-Nowhere PA"
Source B (Healthcare):    "96 First Street, Middle of Nowhere, Pennsylvania"  
Source C (CRM):          "96 first st., middle of nowhere, pa 17001"
```

All three represent the **SAME** physical location, but can only be matched after AI-powered resolution and geocoding.

### Solution Architecture

- **Single FlowEntity Model**: `Location : FlowEntity<Location>` with minimal properties
- **SHA512 Deduplication**: Cache resolution results by normalized address hash
- **Sequential Orchestrator**: Single-threaded processing to prevent race conditions
- **External Identity Preservation**: Leverage Flow's native identity.external space
- **Bidirectional Flow**: Park → Resolve → Imprint → Promote pattern

---

## Quick Start

### Prerequisites
- Docker Desktop
- .NET 9.0 SDK
- Windows (for start.bat)

### Start the Stack
```bash
cd samples/S8.Location
start.bat
```

This will:
1. Build all services (API, adapters)
2. Start MongoDB, RabbitMQ, Ollama
3. Launch location resolution services
4. Open API at http://localhost:4914

### Port Allocation
- **4910**: MongoDB
- **4911**: RabbitMQ AMQP
- **4912**: RabbitMQ Management UI
- **4913**: Ollama AI Service
- **4914**: S8.Location API

---

## Architecture Components

### Core Models

**Location (FlowEntity)**:
```csharp
[Storage("locations", Namespace = "s8")]
public class Location : FlowEntity<Location>
{
    public string Address { get; set; } = "";
    public string? AgnosticLocationId { get; set; } // Canonical reference
    public LocationStatus Status { get; set; } = LocationStatus.Pending;
}
```

**AgnosticLocation (Canonical Storage)**:
```csharp
[Storage("AgnosticLocations", Provider = "Mongo")]
public class AgnosticLocation : Entity<AgnosticLocation>
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string? ParentId { get; set; } // Hierarchical structure
    public LocationType Type { get; set; } // country, state, locality, street, building
    public string Name { get; set; } = "";
    public GeoCoordinate? Coordinates { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
```

### Resolution Pipeline

1. **Normalize**: Convert address to uppercase, remove punctuation, compress whitespace
2. **Hash**: Generate SHA512 of normalized address
3. **Cache Check**: 95%+ hit rate eliminates expensive processing
4. **AI Correction**: Use Ollama to standardize address format
5. **Geocoding**: Google Maps API with OpenStreetMap fallback
6. **Hierarchy Build**: Create country → state → locality → street → building chain
7. **Cache Store**: Store SHA512 → ULID mapping for future lookups

### Flow Processing Pattern

**Park → Resolve → Imprint → Promote**:
1. **Park**: Set status to Parked, halt normal flow
2. **Resolve**: Expensive AI/geocoding operations to get canonical ULID  
3. **Imprint**: Set AgnosticLocationId on the Location entity
4. **Promote**: Set status to Active, resume normal flow

### Source Adapters

**Inventory System Adapter**:
- Sends locations with external ID "IS1", "IS2", etc.
- Stored in Flow's identity.external.inventory space
- Automatic correlation to canonical location

**Healthcare System Adapter**:
- Sends locations with external ID "HP1", "HP2", etc.
- Stored in Flow's identity.external.healthcare space
- Automatic correlation to canonical location

---

## Performance Characteristics

### Cache Performance
```
First occurrence: 0% hit (must resolve)
Second occurrence: 100% hit (SHA512 match)
Overall expected: 95%+ hit rate in production
```

### Processing Times
```
Cache hit: ~5ms (SHA512 compute + cache lookup)
Cache miss: ~500-1500ms (AI + geocoding + hierarchy build)
Average (95% cache hit): ~30ms
```

### Cost Analysis
```
Google Maps Geocoding: $0.005 per address
Ollama (local): $0.0001 per address (compute cost)
With 95% cache hit: $0.00025 per address average
Monthly cost for 1M addresses: ~$250 (vs $5000 without caching)
```

---

## Project Structure

```
samples/S8.Location/
├── S8.Location.Core/                    # Core domain logic
│   ├── Initialization/
│   │   └── SoraAutoRegistrar.cs         # Self-registration
│   ├── Models/
│   │   ├── Location.cs                  # FlowEntity<Location>
│   │   ├── AgnosticLocation.cs          # Canonical storage
│   │   ├── ResolutionCache.cs           # SHA512 cache
│   │   └── LocationEvents.cs            # Flow events
│   ├── Services/
│   │   ├── IAddressResolutionService.cs # Resolution interface
│   │   ├── AddressResolutionService.cs  # SHA512 + AI + geocoding
│   │   ├── IGeocodingService.cs         # Geocoding interface
│   │   └── GoogleMapsGeocodingService.cs # Google Maps impl
│   ├── Orchestration/
│   │   └── LocationOrchestrator.cs      # [FlowOrchestrator] sequential processing
│   └── Options/
│       └── LocationOptions.cs           # Configuration
├── S8.Location.Api/                     # REST API service
│   ├── Controllers/
│   │   └── LocationsController.cs       # Location CRUD API
│   ├── Program.cs                       # API startup
│   └── Dockerfile                       # Container build
├── S8.Location.Adapters.Inventory/      # Inventory system adapter
│   ├── Program.cs                       # [FlowAdapter] background service
│   └── Dockerfile                       # Container build
├── S8.Location.Adapters.Healthcare/     # Healthcare system adapter  
│   ├── Program.cs                       # [FlowAdapter] background service
│   └── Dockerfile                       # Container build
├── S8.Compose/                          # Docker orchestration
│   └── docker-compose.yml              # MongoDB + RabbitMQ + Ollama + services
├── start.bat                           # Windows startup script
├── README.md                           # This file
└── IMPLEMENTATION.md                   # Detailed implementation guide
```

---

## Development Workflow

### Phase 1: Core Infrastructure (1 day)
- Create Location FlowEntity model
- Implement SHA512 normalization and hashing  
- Set up ResolutionCache collection
- Create sequential orchestrator

### Phase 2: Resolution Pipeline (2 days)
- Integrate Sora.AI (Ollama) for address correction
- Add Google Maps geocoding service + fallback
- Build AgnosticLocation hierarchy generator
- Implement caching layer

### Phase 3: Flow Integration (1 day)  
- Configure Flow adapters for source systems
- Set up external identity mappings
- Implement park/promote flow control
- Add event emissions for downstream

### Phase 4: Monitoring & Operations (1 day)
- Add cache hit ratio metrics
- Monitor resolution costs
- Track processing latencies  
- Create operational dashboards

---

## Configuration

### Environment Variables
```bash
# MongoDB
SORA_DATA_MONGO_DATABASE=s8location
SORA_DATA_MONGO_CONNECTIONSTRING=mongodb://mongo:27017

# RabbitMQ  
SORA_MESSAGING_RABBITMQ_CONNECTIONSTRING=amqp://guest:guest@rabbitmq:5672

# AI (Ollama)
SORA_AI_SERVICES_OLLAMA_0_ID=ollama
SORA_AI_SERVICES_OLLAMA_0_BASEURL=http://ollama:11434
SORA_AI_SERVICES_OLLAMA_0_DEFAULTMODEL=llama3.1:8b

# Geocoding
GOOGLE_MAPS_API_KEY=your_api_key_here
LOCATION_MONTHLY_GEOCODING_BUDGET=250.00
```

### appsettings.json
```json
{
  "S8": {
    "Location": {
      "Orchestrator": {
        "ProcessingMode": "Sequential",
        "TimeoutSeconds": 30,
        "MaxRetries": 3
      },
      "Resolution": {
        "CacheEnabled": true,
        "CacheTTLHours": 720,
        "NormalizationRules": {
          "CaseMode": "Upper",
          "RemovePunctuation": true,
          "CompressWhitespace": true
        }
      },
      "Geocoding": {
        "Primary": "GoogleMaps",
        "Fallback": "OpenStreetMap",
        "MaxMonthlyBudget": 250.00
      }
    }
  }
}
```

---

## Monitoring & Health Checks

### Key Metrics
- **Cache Hit Rate**: Target 95%+
- **Resolution Latency**: Target <30ms average
- **Monthly Cost**: Target <$250 for 1M addresses
- **Queue Depth**: Monitor orchestrator backlog
- **Error Rate**: Track resolution failures

### Health Endpoints
- `/health` - Overall system health
- `/health/cache` - Cache availability and hit rates
- `/health/ai` - Ollama model availability  
- `/health/geocoding` - API quota and connectivity
- `/health/queue` - RabbitMQ and orchestrator status

### Dashboards
- **Grafana**: Cache performance, latency, costs
- **RabbitMQ Management**: Message flow and queue depths
- **MongoDB Compass**: Data growth and query performance

---

## Testing Strategy

### Unit Tests
- SHA512 normalization logic
- Address hierarchy building
- Cache hit/miss scenarios
- Mock AI and geocoding responses

### Integration Tests  
- End-to-end adapter → orchestrator → API flow
- MongoDB and RabbitMQ integration
- Ollama AI integration
- Google Maps API integration

### Performance Tests
- Cache hit rate validation
- Latency under load
- Cost optimization verification
- Sequential orchestrator throughput

---

## Deployment

### Local Development
```bash
cd samples/S8.Location
start.bat
```

### Production Deployment
1. Configure environment variables
2. Set up external MongoDB cluster
3. Configure RabbitMQ cluster
4. Deploy Ollama with required models
5. Set Google Maps API key and billing limits
6. Deploy services via container orchestration

---

## Architecture Decision Records

### ADR-001: Single FlowEntity Model
**Decision**: Use minimal `Location : FlowEntity<Location>` with only Address and AgnosticLocationId

**Rationale**: Simplicity over complexity, Flow handles metadata via external identity space

### ADR-002: SHA512 for Deduplication  
**Decision**: Use SHA512 hash of normalized address as cache key

**Rationale**: Eliminates 95% of resolution calls, consistent results, collision-resistant

### ADR-003: Sequential Orchestrator Processing
**Decision**: Process all locations through single-threaded orchestrator

**Rationale**: Eliminates race conditions, ensures consistent ordering, natural queue processing

### ADR-004: Bidirectional Flow Pattern
**Decision**: Park → Resolve → Imprint → Promote pattern

**Rationale**: Clear state transitions, allows async expensive operations, maintains Flow integrity

### ADR-005: External Identity Preservation
**Decision**: Use Flow's native identity.external space for source IDs

**Rationale**: Built-in Flow capability, maintains perfect traceability, no custom mapping needed

---

## Next Steps

1. **Implementation**: Follow `IMPLEMENTATION.md` for step-by-step development guide
2. **Framework Integration**: Leverage existing Sora capabilities (no changes required)
3. **Testing**: Implement comprehensive test suite
4. **Monitoring**: Set up observability and alerting
5. **Production**: Deploy with proper configuration and scaling

---

## Related Documents

- **CLD_ORCHESTRATOR_BIDIRECTIONAL_PATTERN.md**: Detailed architecture specification
- **CLD_LOCATION_STANDARDIZATION.md**: Complete system design and rationale
- **IMPLEMENTATION.md**: Step-by-step development guide
- **S8.Flow**: Reference implementation for Flow patterns
- **S5.Recs**: Reference implementation for AI integration

---

**Document Status**: Ready for Implementation  
**Architecture Status**: Final - Gang Approved ✅  
**Framework Dependencies**: Sora.Data, Sora.Flow, Sora.Messaging, Sora.AI