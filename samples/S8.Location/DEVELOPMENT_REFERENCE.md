# S8.Location Development Reference

**Quick reference for future coding sessions**

---

## Project Structure

```
samples/S8.Location/
├── README.md                   # Complete project overview
├── IMPLEMENTATION.md           # Step-by-step development guide  
├── start.bat                   # One-command stack startup
├── S8.Location.Core/           # Domain logic & self-registration
├── S8.Location.Api/           # REST API service
├── S8.Location.Adapters.*/    # Source system adapters
└── S8.Compose/                # Docker orchestration
```

---

## Key Architecture Patterns

### 1. Self-Registration Pattern
```csharp
// S8.Location.Core/Initialization/SoraAutoRegistrar.cs
public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public void Initialize(IServiceCollection services)
    {
        services.AddSoraOptions<LocationOptions>();
        services.AddScoped<IAddressResolutionService, AddressResolutionService>();
        // ... automatic discovery and registration
    }
}
```

### 2. FlowEntity + Entity Pattern
```csharp
// Location gets both data persistence AND messaging capabilities
[Storage("locations", Namespace = "s8")]
public class Location : FlowEntity<Location>
{
    public string Address { get; set; } = "";
    public string? AgnosticLocationId { get; set; }
    public LocationStatus Status { get; set; }
}

// Canonical storage - pure data entity
[Storage("AgnosticLocations", Provider = "Mongo")]  
public class AgnosticLocation : Entity<AgnosticLocation>
{
    // Hierarchical location storage
}
```

### 3. SHA512 Deduplication Strategy
```csharp
public async Task<string> ResolveToCanonicalIdAsync(string address)
{
    var normalized = NormalizeAddress(address);
    var sha512 = ComputeSHA512(normalized);
    
    // 95% cache hit rate eliminates expensive AI/geocoding
    var cached = await _cache.GetAsync(sha512);
    if (cached != null) return cached.CanonicalUlid;
    
    // Only resolve if not cached
    // ... expensive AI + geocoding operations
}
```

### 4. Sequential Orchestrator Pattern
```csharp
[FlowOrchestrator]
public class LocationOrchestrator : IFlowOrchestrator<Location>
{
    private readonly SemaphoreSlim _processLock = new(1, 1); // Sequential!
    
    public async Task ProcessAsync(Location location, FlowContext context)
    {
        await _processLock.WaitAsync();
        try
        {
            // Park → Resolve → Imprint → Promote
            location.Status = LocationStatus.Parked;
            var canonicalId = await _resolver.ResolveToCanonicalIdAsync(location.Address);
            location.AgnosticLocationId = canonicalId;
            location.Status = LocationStatus.Active;
        }
        finally { _processLock.Release(); }
    }
}
```

### 5. FlowAdapter Pattern
```csharp
[FlowAdapter(system: "inventory", adapter: "inventory", DefaultSource = "inventory")]
public sealed class InventoryLocationAdapter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var location = new Location 
        { 
            Id = "IS1", // Stored in identity.external.inventory
            Address = "96 1st street Middle-of-Nowhere PA"
        };
        await location.Send(); // Auto-routing to orchestrator
    }
}
```

---

## Core Services

### Address Resolution Service
- **Purpose**: SHA512 caching + AI correction + geocoding
- **Key Method**: `ResolveToCanonicalIdAsync(string address)`
- **Cache Hit Rate**: Target 95%+
- **Cost Optimization**: $250/month vs $5000/month for 1M addresses

### Location Orchestrator  
- **Purpose**: Sequential processing to prevent race conditions
- **Pattern**: Park → Resolve → Imprint → Promote
- **Thread Safety**: SemaphoreSlim(1,1) ensures single-threaded processing

### Source Adapters
- **Inventory**: External IDs "IS1", "IS2", etc.
- **Healthcare**: External IDs "HP1", "HP2", etc.
- **Flow Integration**: Identity.external space preserves source attribution

---

## Development Commands

### Start Complete Stack
```bash
cd samples/S8.Location
start.bat
```

### Access Services
- **API**: http://localhost:4914
- **Swagger**: http://localhost:4914/swagger
- **RabbitMQ**: http://localhost:4912 (guest/guest)
- **MongoDB**: localhost:4910
- **Ollama**: http://localhost:4913

### View Logs
```bash
docker logs s8-location-api
docker logs s8-location-adapter-inventory
docker logs s8-location-adapter-healthcare
```

### Stop Stack
```bash
docker compose -p sora-s8-location down
```

---

## Configuration Keys

### Environment Variables
```bash
# Core Framework
SORA_DATA_MONGO_DATABASE=s8location
SORA_MESSAGING_RABBITMQ_CONNECTIONSTRING=amqp://guest:guest@rabbitmq:5672

# AI Integration
SORA_AI_SERVICES_OLLAMA_0_BASEURL=http://ollama:11434
SORA_AI_SERVICES_OLLAMA_0_DEFAULTMODEL=llama3.1:8b

# Location-Specific
S8_LOCATION_RESOLUTION_CACHEENABLED=true
S8_LOCATION_GEOCODING_GOOGLEMAPSAPIKEY=your_key_here
GOOGLE_MAPS_API_KEY=your_key_here
```

### appsettings.json
```json
{
  "S8": {
    "Location": {
      "Resolution": { "CacheEnabled": true, "CacheTTLHours": 720 },
      "Geocoding": { "Primary": "GoogleMaps", "MaxMonthlyBudget": 250.00 }
    }
  }
}
```

---

## Data Models

### Location (FlowEntity)
- **Address**: Raw address from source system
- **AgnosticLocationId**: Reference to canonical location
- **Status**: Pending → Parked → Active

### AgnosticLocation (Entity)
- **Hierarchical**: ParentId for country → state → locality → street
- **Coordinates**: Lat/Lng from geocoding
- **Metadata**: Flexible key-value storage

### ResolutionCache (Entity)
- **Id**: SHA512 hash of normalized address
- **CanonicalUlid**: Reference to AgnosticLocation
- **95% Hit Rate**: Eliminates expensive resolution calls

---

## Integration Points

### Sora.Data
- `Entity<T>` base class with MongoDB via `[Storage]` attributes
- Automatic CRUD: `Location.Get()`, `Location.All()`, `location.Save()`

### Sora.Flow  
- `FlowEntity<T>` adds messaging: `await location.Send()`
- `[FlowOrchestrator]` automatic discovery and registration
- Identity.external space for source system correlation

### Sora.Messaging
- RabbitMQ integration with automatic envelope wrapping
- Convention-based queue naming with `.transport` suffix

### Sora.AI
- Ollama integration for address correction
- `IAi.PromptAsync()` for standardizing address formats

---

## Testing Strategy

### Unit Tests
- SHA512 normalization logic
- Cache hit/miss scenarios  
- Address hierarchy building

### Integration Tests
- End-to-end adapter → orchestrator → API flow
- External service integration (MongoDB, RabbitMQ, Ollama)

### Performance Tests
- Cache hit rate validation (target 95%+)
- Processing latency (target <30ms average)
- Cost optimization verification

---

## Monitoring & Health

### Health Endpoints
- `/health` - Overall system health
- `/health/cache` - Cache performance
- `/health/ai` - Ollama availability
- `/health/geocoding` - API quotas

### Key Metrics
- **Cache Hit Rate**: 95%+ target
- **Processing Latency**: <30ms average  
- **Monthly Cost**: <$250 for 1M addresses
- **Queue Depth**: Monitor orchestrator backlog

### Dashboards
- **Cache Performance**: Hit rates, entry counts
- **Resolution Costs**: Google Maps API usage
- **Flow Processing**: Latencies, error rates

---

## Architecture Decision Records

### ADR-001: Single FlowEntity Model
**Rationale**: Simplicity over complexity, Flow handles metadata

### ADR-002: SHA512 Deduplication
**Rationale**: 95% cost reduction, consistent results

### ADR-003: Sequential Processing  
**Rationale**: Eliminates race conditions completely

### ADR-004: Bidirectional Flow
**Rationale**: Clear state transitions, async expensive operations

### ADR-005: External Identity Preservation
**Rationale**: Built-in Flow capability, perfect traceability

---

## Common Tasks

### Add New Source Adapter
1. Create new `S8.Location.Adapters.NewSource` project
2. Implement `[FlowAdapter]` background service
3. Add to docker-compose.yml
4. Update start.bat if needed

### Modify Resolution Logic
- Edit `AddressResolutionService.cs`
- Update normalization rules in `LocationOptions.cs`
- Test cache invalidation if needed

### Add New API Endpoints
- Extend `LocationsController.cs`
- Follow RESTful patterns
- Use `Location.Get()`, `Location.Query()` from Entity<T>

### Scale Performance
- Monitor cache hit rates
- Adjust orchestrator timeout settings
- Consider multiple orchestrator instances (future)

---

## Related Documentation

- **CLD_ORCHESTRATOR_BIDIRECTIONAL_PATTERN.md**: Complete architecture spec
- **CLD_LOCATION_STANDARDIZATION.md**: Full system design document  
- **samples/S8.Flow**: Reference Flow implementation patterns
- **samples/S5.Recs**: Reference AI integration patterns

---

## Development Next Steps

1. **Phase 1**: Implement core models and self-registration
2. **Phase 2**: Build resolution pipeline with caching  
3. **Phase 3**: Create Flow orchestrator
4. **Phase 4**: Build source adapters
5. **Phase 5**: Create API service
6. **Phase 6**: Docker Compose deployment
7. **Phase 7**: Testing and monitoring

**Total Effort**: 8 development days  
**Framework Changes**: None required  
**Dependencies**: Google Maps API key (optional)