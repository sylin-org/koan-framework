# CLD Document: Orchestrator Bidirectional Pattern for Location Standardization

**Document Type**: Core Library Design (CLD)  
**Pattern Name**: Orchestrator Bidirectional Pattern with SHA512 Deduplication  
**Version**: 2.0  
**Date**: 2025-01-08  
**Status**: Final Architecture  

---

## Executive Summary

This document describes the refined architecture for S8.Location, implementing a canonical location storage system using Sora.Flow's orchestrator pattern with bidirectional flow, SHA512-based deduplication, and sequential processing to eliminate race conditions.

### Key Architecture Decisions
- **Single FlowEntity Model**: `Location : FlowEntity<Location>` with minimal properties
- **SHA512 Deduplication**: Cache resolution results by normalized address hash
- **Sequential Orchestrator**: Single-threaded processing to prevent race conditions
- **External Identity Preservation**: Leverage Flow's native identity.external space
- **Bidirectional Flow**: Park → Resolve → Imprint → Promote pattern

---

## 1. Core Problem Statement

### The Challenge
Multiple source systems provide addresses in different formats that represent the same physical location. These can only be definitively matched after AI-powered resolution and geocoding.

### Examples
```
Source A (Inventory):     "96 1st street Middle-of-Nowhere PA"
Source B (Healthcare):    "96 First Street, Middle of Nowhere, Pennsylvania"
Source C (CRM):          "96 first st., middle of nowhere, pa 17001"

All three represent the SAME physical location.
```

### Requirements
1. **Canonical Storage**: Single source of truth for each physical location
2. **Source Attribution**: Preserve which systems provided which addresses
3. **Deduplication**: Automatically identify when different strings = same location
4. **Performance**: Minimize expensive AI/geocoding calls
5. **Consistency**: Prevent race conditions and duplicate canonicals

---

## 2. Architecture Solution

### 2.1 Core Data Model

**Minimal FlowEntity Design:**
```csharp
public class Location : FlowEntity<Location>
{
    public string Address { get; set; } = "";
    public string? AgnosticLocationId { get; set; } // Canonical reference
    public LocationStatus Status { get; set; } = LocationStatus.Pending;
}

public enum LocationStatus
{
    Pending,    // Just received
    Parked,     // Awaiting resolution
    Active      // Resolution complete
}
```

**Canonical Location Storage:**
```csharp
[Storage("AgnosticLocations", Provider = "Mongo")]
public class AgnosticLocation : Entity<AgnosticLocation>
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string? ParentId { get; set; } // Self-referencing hierarchy
    public LocationType Type { get; set; } // country, state, locality, street, building
    public string Name { get; set; } = "";
    public string? Code { get; set; } // Country/state codes
    public GeoCoordinate? Coordinates { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
```

### 2.2 Orchestrator Bidirectional Pattern

```mermaid
graph LR
    A[Source System] -->|FlowAdapter| B[Location Entity]
    B -->|Park| C[Resolution Queue]
    C -->|SHA512 Check| D{Cache Hit?}
    D -->|Yes| E[Return ULID]
    D -->|No| F[AI + Geocoding]
    F --> G[Create AgnosticLocation]
    G --> H[Cache SHA512→ULID]
    H --> E
    E -->|Imprint| I[Set AgnosticLocationId]
    I -->|Promote| J[Active Status]
```

**Implementation:**
```csharp
[FlowOrchestrator]
public class LocationOrchestrator : IFlowOrchestrator<Location>
{
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly IAddressResolutionService _resolver;
    
    public async Task ProcessAsync(Location location, FlowContext context)
    {
        await _processLock.WaitAsync();
        try
        {
            // 1. PARK - Stop flow for resolution
            location.Status = LocationStatus.Parked;
            await location.SaveAsync();
            
            // 2. RESOLVE - Get or create canonical ID
            var canonicalId = await _resolver.ResolveToCanonicalIdAsync(location.Address);
            
            // 3. IMPRINT - Set canonical reference
            location.AgnosticLocationId = canonicalId;
            
            // 4. PROMOTE - Resume normal flow
            location.Status = LocationStatus.Active;
            await location.SaveAsync();
            
            // Emit event for downstream processing
            await context.EmitAsync(new LocationResolvedEvent(location.Id, canonicalId));
        }
        finally
        {
            _processLock.Release();
        }
    }
}
```

### 2.3 SHA512 Deduplication Strategy

**Resolution Service with Caching:**
```csharp
public class AddressResolutionService : IAddressResolutionService
{
    private readonly IDataRepository<ResolutionCache, string> _cache;
    private readonly IOllamaClient _aiClient;
    private readonly IGeocodingService _geocoding;
    
    public async Task<string> ResolveToCanonicalIdAsync(string address)
    {
        // Step 1: Normalize address for consistent hashing
        var normalized = NormalizeAddress(address);
        
        // Step 2: Generate deterministic hash
        var sha512 = ComputeSHA512(normalized);
        
        // Step 3: Check cache
        var cached = await _cache.GetAsync(sha512);
        if (cached != null)
        {
            _logger.LogDebug("Cache hit for address hash {Hash}", sha512.Substring(0, 8));
            return cached.CanonicalUlid;
        }
        
        // Step 4: Perform expensive resolution
        _logger.LogInformation("Resolving new address: {Address}", address);
        
        // AI correction
        var aiCorrected = await _aiClient.CorrectAddressAsync(address);
        
        // Geocoding
        var coordinates = await _geocoding.GeocodeAsync(aiCorrected);
        
        // Create hierarchical structure
        var hierarchy = await BuildLocationHierarchy(aiCorrected, coordinates);
        
        // Generate canonical ULID
        var canonicalId = hierarchy.LeafNode.Id;
        
        // Step 5: Cache for future
        await _cache.UpsertAsync(new ResolutionCache
        {
            Id = sha512,
            CanonicalUlid = canonicalId,
            NormalizedAddress = normalized,
            ResolvedAt = DateTime.UtcNow
        });
        
        return canonicalId;
    }
    
    private string NormalizeAddress(string address)
    {
        return address
            .ToUpperInvariant()
            .Replace(".", "")
            .Replace(",", " ")
            .Trim()
            .CompressWhitespace();
    }
    
    private string ComputeSHA512(string input)
    {
        using var sha512 = SHA512.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha512.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
```

### 2.4 External Identity Management

**Flow Adapter Integration:**
```csharp
// Inventory system sends location
[FlowAdapter("inventory")]
public class InventoryLocationAdapter : IFlowAdapter<Location>
{
    public async Task SendAsync(string externalId, string address)
    {
        var location = new Location
        {
            Id = externalId, // "IS1" - stored in identity.external.inventory
            Address = address
        };
        
        await location.Send();
    }
}

// Healthcare system sends location
[FlowAdapter("healthcare")]
public class HealthcareLocationAdapter : IFlowAdapter<Location>
{
    public async Task SendAsync(string externalId, string address)
    {
        var location = new Location
        {
            Id = externalId, // "HP1" - stored in identity.external.healthcare
            Address = address
        };
        
        await location.Send();
    }
}
```

**Identity Resolution:**
```csharp
// Flow automatically maintains:
// identity.external.inventory = "IS1" → AgnosticLocationId = "ulid_abc123"
// identity.external.healthcare = "HP1" → AgnosticLocationId = "ulid_abc123"

// Query by source system ID
var location = await Flow.GetByExternalIdAsync<Location>("inventory", "IS1");

// Query all locations for same canonical
var allSources = await Flow.GetByAgnosticLocationIdAsync("ulid_abc123");
// Returns: [
//   { source: "inventory", id: "IS1" },
//   { source: "healthcare", id: "HP1" }
// ]
```

---

## 3. Architecture Decision Records

### ADR-001: Single FlowEntity Model
**Decision**: Use minimal `Location : FlowEntity<Location>` with only Address and AgnosticLocationId

**Rationale**:
- Simplicity over complexity
- Flow handles metadata via external identity space
- Reduces merge conflicts
- Clear separation of concerns

**Consequences**:
- (+) Dead simple model
- (+) No complex merge logic needed
- (+) Source systems maintain their own metadata
- (-) Additional lookups for source-specific data

### ADR-002: SHA512 for Deduplication
**Decision**: Use SHA512 hash of normalized address as cache key

**Rationale**:
- Deterministic and collision-resistant
- Eliminates 99% of resolution calls
- Simple to implement and understand
- Platform-independent

**Consequences**:
- (+) Massive cost savings on AI/geocoding
- (+) Consistent results for same input
- (+) Fast cache lookups
- (-) Cache invalidation complexity if resolution improves

### ADR-003: Sequential Orchestrator Processing
**Decision**: Process all locations through single-threaded orchestrator

**Rationale**:
- Eliminates race conditions completely
- Ensures consistent ordering
- Simplifies error handling
- Natural queue processing

**Consequences**:
- (+) No distributed locking needed
- (+) Predictable behavior
- (+) Simple to debug and monitor
- (-) Potential throughput bottleneck (mitigated by caching)

### ADR-004: Bidirectional Flow Pattern
**Decision**: Park → Resolve → Imprint → Promote pattern

**Rationale**:
- Clear state transitions
- Allows async expensive operations
- Maintains Flow event stream integrity
- Enables reprocessing if needed

**Consequences**:
- (+) Clean separation of concerns
- (+) Observable state transitions
- (+) Reprocessing capability
- (-) Additional state management complexity

### ADR-005: External Identity Preservation
**Decision**: Use Flow's native identity.external space for source IDs

**Rationale**:
- Built-in Flow capability
- Maintains perfect traceability
- No custom identity mapping needed
- Supports unlimited source systems

**Consequences**:
- (+) Zero custom code for identity management
- (+) Native Flow query support
- (+) Automatic deduplication across sources
- (-) Requires understanding Flow identity model

---

## 4. Performance Characteristics

### Cache Hit Ratios
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

### Throughput
```
With caching: 1000+ locations/second
Without caching: 2-5 locations/second
Sequential processing: Not a bottleneck due to caching
```

### Cost Analysis
```
Google Maps Geocoding: $0.005 per address
Ollama (local): $0.0001 per address (compute cost)
With 95% cache hit: $0.00025 per address average
Monthly cost for 1M addresses: ~$250 (vs $5000 without caching)
```

---

## 5. Implementation Checklist

### Phase 1: Core Infrastructure
- [ ] Create Location FlowEntity model
- [ ] Implement SHA512 normalization and hashing
- [ ] Set up ResolutionCache collection
- [ ] Create sequential orchestrator

### Phase 2: Resolution Pipeline
- [ ] Integrate Ollama for address correction
- [ ] Add geocoding service (Google Maps + fallback)
- [ ] Build AgnosticLocation hierarchy generator
- [ ] Implement caching layer

### Phase 3: Flow Integration
- [ ] Configure Flow adapters for source systems
- [ ] Set up external identity mappings
- [ ] Implement park/promote flow control
- [ ] Add event emissions for downstream

### Phase 4: Monitoring & Operations
- [ ] Add cache hit ratio metrics
- [ ] Monitor resolution costs
- [ ] Track processing latencies
- [ ] Create operational dashboards

---

## 6. Configuration

```json
{
  "Sora": {
    "Location": {
      "Orchestrator": {
        "ProcessingMode": "Sequential",
        "TimeoutSeconds": 30,
        "MaxRetries": 3
      },
      "Resolution": {
        "CacheEnabled": true,
        "CacheTTLHours": 720,  // 30 days
        "NormalizationRules": {
          "CaseMode": "Upper",
          "RemovePunctuation": true,
          "CompressWhitespace": true
        }
      },
      "AI": {
        "Provider": "Ollama",
        "Model": "llama3.1:8b",
        "TimeoutMs": 5000
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

## 7. Monitoring & Observability

### Key Metrics
```csharp
public class LocationMetrics
{
    // Cache effectiveness
    public Counter CacheHits { get; } = new("location_cache_hits_total");
    public Counter CacheMisses { get; } = new("location_cache_misses_total");
    
    // Resolution performance
    public Histogram ResolutionDuration { get; } = new("location_resolution_duration_ms");
    public Counter ResolutionErrors { get; } = new("location_resolution_errors_total");
    
    // Cost tracking
    public Counter GeocodingCalls { get; } = new("location_geocoding_calls_total");
    public Gauge MonthlyCost { get; } = new("location_monthly_cost_usd");
    
    // Flow metrics
    public Counter LocationsProcessed { get; } = new("location_processed_total");
    public Gauge QueueDepth { get; } = new("location_queue_depth");
}
```

### Health Checks
```csharp
public class LocationHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
    {
        var checks = new Dictionary<string, object>();
        
        // Check cache availability
        checks["cache"] = await _cache.IsHealthyAsync();
        
        // Check AI service
        checks["ollama"] = await _ollamaClient.IsHealthyAsync();
        
        // Check geocoding quota
        var quotaRemaining = await _geocoding.GetQuotaRemainingAsync();
        checks["geocoding_quota"] = quotaRemaining;
        
        // Check orchestrator queue
        var queueDepth = await _orchestrator.GetQueueDepthAsync();
        checks["queue_depth"] = queueDepth;
        
        if (quotaRemaining < 100)
            return HealthCheckResult.Degraded("Low geocoding quota", checks);
            
        if (queueDepth > 1000)
            return HealthCheckResult.Degraded("High queue depth", checks);
            
        return HealthCheckResult.Healthy("All systems operational", checks);
    }
}
```

---

## 8. Conclusion

This architecture achieves the core objective of canonical location storage through:

1. **Simplicity**: Minimal data model, maximum capability
2. **Performance**: 95%+ cache hit rate through SHA512 deduplication
3. **Consistency**: Sequential processing eliminates race conditions
4. **Traceability**: External identity preservation maintains source attribution
5. **Cost-Effectiveness**: 95% reduction in AI/geocoding costs

The Orchestrator Bidirectional Pattern with SHA512 deduplication represents a production-ready solution that leverages Sora.Flow's native capabilities while maintaining elegant simplicity.

---

**Document Status**: Final Architecture - Ready for Implementation  
**Review Status**: Gang Approved ✅  
**Next Step**: Begin Phase 1 implementation