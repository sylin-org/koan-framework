# AI Source Routing Architecture: Findings and Proposed Fix

**Date:** 2025-10-01
**Issue:** Adapter vs Source lifecycle confusion causing router resolution failures
**Status:** ~~Implementation Pending~~ **SUPERSEDED**

---

## 🔴 SUPERSEDED BY CANONICAL MODEL

**This document identified architectural issues that led to establishing the canonical source-member model.**

**NEW CANONICAL REFERENCE:** [AI-SOURCE-MEMBER-ARCHITECTURE.md](AI-SOURCE-MEMBER-ARCHITECTURE.md)

**Key Realizations:**
1. Original "Source" concept was actually **Member** (endpoint)
2. Original "Group" concept was actually **Source** (collection)
3. Adapter (HOW) vs Source (WHERE) separation was correct principle
4. Implementation needed complete terminology realignment

**This Document's Value:** Historical context for why the canonical model was established.

**For Implementation:** See canonical document above and ADR-0014 amendment.

---

---

## Executive Summary

The current implementation violates the **Adapter (HOW) vs Source (WHERE)** separation principle by creating multiple adapter instances per discovered URL. This causes router failures when resolving source monikers to actual connections.

**Core Principle Violated:**
- ❌ Current: Multiple `OllamaAdapter` instances with same ID "ollama"
- ✅ Correct: ONE adapter instance (protocol), MULTIPLE sources (endpoints)

---

## Architectural Principles (Correct)

### Adapter = Protocol Handler (Singleton)
```
AdapterId: "ollama"
Purpose: Defines HOW to communicate (protocol, serialization, API contract)
Lifecycle: Registered ONCE per application
Example: OllamaAdapter knows how to speak Ollama's HTTP/JSON API
```

### Source = Endpoint Instance (Multiple)
```
Source Name: "enterprise" | "ollama" | "koan-auto-host"
Purpose: Defines WHERE to connect (URL, credentials, capabilities)
Lifecycle: Multiple per adapter, registered by config or discovery
Example:
  - "enterprise" → http://prod-ollama:11434
  - "koan-auto-host" → http://host.docker.internal:11434
```

### Resolution Flow
```
1. User Request: Ai.Embed("text", source: "enterprise")
2. Router: Resolve "enterprise" → AiSourceDefinition (fast hashmap lookup)
3. Source: Extract Provider="ollama", ConnectionString="http://prod:11434"
4. Adapter Registry: Get adapter by Provider="ollama"
5. Adapter: Execute request using ConnectionString from source
```

---

## Current Implementation Violations

### Violation 1: Multiple Adapter Instances Created

**Location:** `src/Connectors/AI/Ollama/Initialization/OllamaDiscoveryService.cs:570`

```csharp
private async Task DiscoverAndRegisterMultipleSources(...)
{
    foreach (var (name, url, priority, origin) in discoveredSources)
    {
        // ✅ Correct: Register source metadata
        _sourceRegistry!.RegisterSource(source);

        // ❌ WRONG: Creates duplicate adapter instance per URL
        await RegisterOllamaAdapter(url, defaultModel, cancellationToken);
    }
}
```

**Result:** Multiple `OllamaAdapter` instances registered with ID "ollama", causing registry conflicts.

---

### Violation 2: Adapter Has Immutable URL

**Location:** `src/Connectors/AI/Ollama/OllamaAdapter.cs:57-68`

```csharp
public OllamaAdapter(HttpClient http, ILogger<OllamaAdapter> logger, ...)
{
    _http = http; // BaseAddress is fixed at construction time
    // Cannot serve multiple sources with different URLs
}
```

**Impact:** Forces one adapter instance per URL, violating singleton principle.

---

### Violation 3: Wrong Default Source Name

**Location:** `src/Koan.AI/Sources/AiSourceRegistry.cs:41-54`

```csharp
if (!_sources.ContainsKey("Default"))
{
    RegisterSource(new AiSourceDefinition
    {
        Name = "Default",  // ❌ Should be provider-specific: "ollama"
        Provider = "",     // ❌ Empty provider
        Capabilities = new Dictionary<string, AiCapabilityConfig>()
    });
}
```

**Result:** Router tries to match "Default" source with empty provider, fails to find adapter.

---

### Violation 4: Router Can't Match Adapter to Source

**Location:** `src/Koan.AI/DefaultAiRouter.cs:501-554`

**Current Logic:**
```csharp
private IAiAdapter? FindAdapterForSource(AiSourceDefinition source)
{
    // Tries to parse URL and match against adapter ID string patterns
    // Fails because all adapters have ID="ollama", not "ollama@host:port"
    if (adapter.Id.Contains($"{sourceHost}:{sourcePort}"))
        return adapter;
}
```

**Problem:** Tries to find adapter by URL pattern matching instead of `source.Provider` field.

---

## Proposed Fix

### Phase 1: Source Moniker Routing

**1.1 Add SourceName to Request Routing**

```csharp
// File: src/Koan.AI.Contracts/Models/AiRoute.cs
public record AiRoute
{
    /// <summary>
    /// Source moniker to route to: "enterprise", "ollama", "koan-auto-host"
    /// If null, router selects highest-priority source with required capability
    /// </summary>
    public string? SourceName { get; init; }

    /// <summary>
    /// Adapter ID hint (backward compatibility)
    /// </summary>
    public string? AdapterId { get; init; }

    /// <summary>
    /// Routing policy: "round-robin", "priority", "weighted-priority"
    /// </summary>
    public string? Policy { get; init; }
}
```

**1.2 Update Request Models**

```csharp
// Usage examples:
Ai.Embed("text")  // Uses default source (highest priority)
Ai.Embed("text", new AiEmbeddingsRequest { Route = new AiRoute { SourceName = "enterprise" } })
Ai.Chat("prompt", options: new AiChatOptions { Source = "koan-auto-host" })
```

---

### Phase 2: Adapter Singleton Pattern

**2.1 Register Adapter ONCE in KoanAutoRegistrar**

```csharp
// File: src/Connectors/AI/Ollama/Initialization/KoanAutoRegistrar.cs
public void Initialize(IServiceCollection services)
{
    // Register singleton adapter factory
    services.AddSingleton<IOllamaAdapterFactory, OllamaAdapterFactory>();

    // Register adapter discovery (creates sources, NOT adapters)
    services.AddHostedService<OllamaDiscoveryService>();
}
```

**2.2 Remove Adapter Creation from Discovery Loop**

```csharp
// File: src/Connectors/AI/Ollama/Initialization/OllamaDiscoveryService.cs
private async Task DiscoverAndRegisterMultipleSources(...)
{
    foreach (var (name, url, priority, origin) in discoveredSources)
    {
        // ✅ Register source metadata
        _sourceRegistry!.RegisterSource(new AiSourceDefinition
        {
            Name = name,
            Provider = "ollama",  // Links to adapter
            ConnectionString = url,
            Priority = priority,
            Capabilities = capabilities
        });

        // ❌ REMOVE THIS:
        // await RegisterOllamaAdapter(url, defaultModel, cancellationToken);
    }
}
```

---

### Phase 3: Adapter URL Resolution

**Option A: HttpClient Pool (Recommended)**

```csharp
// File: src/Connectors/AI/Ollama/OllamaAdapter.cs
public class OllamaAdapter : IAiAdapter
{
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly ConcurrentDictionary<string, HttpClient> _clientPool = new();
    private readonly IHttpClientFactory _httpClientFactory;

    public async Task<AiEmbeddingsResponse> EmbedAsync(
        AiEmbeddingsRequest request,
        CancellationToken ct)
    {
        // 1. Resolve source moniker
        var sourceName = request.Route?.SourceName ?? "ollama"; // Default to provider
        var source = _sourceRegistry.GetSource(sourceName);

        if (source == null)
            throw new InvalidOperationException($"Source '{sourceName}' not found");

        // 2. Get HttpClient for this URL (pooled)
        var client = GetOrCreateClient(source.ConnectionString);

        // 3. Execute request
        var response = await client.PostAsJsonAsync("/api/embeddings", ...);
        return ParseResponse(response);
    }

    private HttpClient GetOrCreateClient(string baseUrl)
    {
        return _clientPool.GetOrAdd(baseUrl, url =>
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(url);
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
        });
    }
}
```

**Option B: Pass Source to Adapter Methods**

```csharp
// Modify IAiAdapter interface
public interface IAiAdapter
{
    Task<AiEmbeddingsResponse> EmbedAsync(
        AiEmbeddingsRequest request,
        AiSourceDefinition source,  // New parameter
        CancellationToken ct);
}
```

---

### Phase 4: Fix Default Source Naming

**4.1 Provider-Specific Defaults**

```csharp
// File: src/Koan.AI/Sources/AiSourceRegistry.cs
public void DiscoverFromConfiguration(IConfiguration config, ILogger? logger)
{
    // Discover explicit sources
    DiscoverExplicitSources(config, logger);

    // Handle legacy Koan:Ai:Ollama config
    DiscoverLegacyOllamaConfig(config, logger);

    // ❌ REMOVE implicit "Default" creation
    // Each adapter should register its own default source
}
```

**4.2 Adapter Registers Provider Default**

```csharp
// File: src/Connectors/AI/Ollama/Initialization/OllamaDiscoveryService.cs
private async Task RegisterProviderDefaultSource()
{
    var options = _sp.GetService<IOptions<OllamaOptions>>()?.Value;

    // Create "ollama" default source from config or discovery
    var defaultSource = new AiSourceDefinition
    {
        Name = "ollama",  // ✅ Provider-specific name
        Provider = "ollama",
        ConnectionString = options.ConnectionString ?? discoveredUrl,
        Capabilities = capabilities,
        Priority = 50,  // Default priority
        Origin = "provider-default"
    };

    _sourceRegistry.RegisterSource(defaultSource);
}
```

---

### Phase 5: Router Source Resolution

**5.1 Fix Adapter Lookup by Provider**

```csharp
// File: src/Koan.AI/DefaultAiRouter.cs
private IAiAdapter? FindAdapterForSource(AiSourceDefinition source)
{
    // ❌ OLD: String pattern matching on adapter ID
    // if (adapter.Id.Contains($"{sourceHost}:{sourcePort}"))

    // ✅ NEW: Direct provider lookup
    if (string.IsNullOrWhiteSpace(source.Provider))
    {
        _logger?.LogWarning("Source '{SourceName}' has no provider configured", source.Name);
        return _registry.All.FirstOrDefault(); // Fallback
    }

    // Get adapter by provider name
    var adapter = _registry.Get(source.Provider);

    if (adapter == null)
    {
        _logger?.LogWarning("No adapter found for provider '{Provider}' (source: '{SourceName}')",
            source.Provider, source.Name);
        return null;
    }

    _logger?.LogDebug("Matched source '{SourceName}' to adapter '{AdapterId}' (provider: '{Provider}')",
        source.Name, adapter.Id, source.Provider);

    return adapter;
}
```

---

## Implementation Checklist

### Must-Have (Breaks Current Functionality)
- [ ] **Remove** `RegisterOllamaAdapter()` call from discovery loop (OllamaDiscoveryService.cs:570)
- [ ] **Add** `IAiSourceRegistry` injection to `OllamaAdapter` constructor
- [ ] **Add** HttpClient pool to `OllamaAdapter` for multi-URL support
- [ ] **Fix** `FindAdapterForSource()` to use `source.Provider` instead of URL pattern matching
- [ ] **Register** "ollama" default source instead of "Default" empty source

### Should-Have (Architectural Improvements)
- [ ] Add `SourceName` field to `AiRoute` record
- [ ] Add `Source` property to `AiChatOptions` and `AiEmbeddingsOptions` helper classes
- [ ] Document source moniker resolution in XML comments
- [ ] Add fast-fail validation: source exists, provider matches, URL valid

### Nice-to-Have (Future Enhancements)
- [ ] Connection pooling metrics per source
- [ ] Source health tracking in `ISourceHealthRegistry`
- [ ] Automatic source priority adjustment based on health
- [ ] Source aliasing: "primary" → "koan-auto-host"

---

## Testing Strategy

### Unit Tests
```csharp
[Fact]
public void SourceRegistry_ResolvesByMoniker()
{
    var registry = new AiSourceRegistry();
    registry.RegisterSource(new AiSourceDefinition
    {
        Name = "enterprise",
        Provider = "ollama",
        ConnectionString = "http://prod:11434"
    });

    var source = registry.GetSource("enterprise");
    Assert.NotNull(source);
    Assert.Equal("ollama", source.Provider);
}

[Fact]
public void Adapter_HandlesMultipleSources()
{
    var adapter = CreateAdapter();

    var response1 = await adapter.EmbedAsync(new AiEmbeddingsRequest
    {
        Route = new AiRoute { SourceName = "koan-auto-host" }
    });

    var response2 = await adapter.EmbedAsync(new AiEmbeddingsRequest
    {
        Route = new AiRoute { SourceName = "koan-auto-container" }
    });

    Assert.NotNull(response1);
    Assert.NotNull(response2);
}
```

### Integration Tests
```csharp
[Fact]
public async Task Router_ResolvesSourceMoniker_ToCorrectEndpoint()
{
    // Setup: Register sources with different URLs
    // Act: Send request to specific source
    // Assert: Verify correct URL was called (mock HttpClient)
}

[Fact]
public async Task Router_UsesHighestPriority_WhenNoSourceSpecified()
{
    // Setup: Multiple sources with different priorities
    // Act: Send request without source moniker
    // Assert: Highest priority source used
}
```

---

## Migration Path

### Backward Compatibility
- Keep `AiRoute.AdapterId` for existing code
- Auto-map `AdapterId` to source name if `SourceName` is null
- Deprecation warning in v0.7, removal in v1.0

### Configuration Migration
```json
// OLD (still supported):
{
  "Koan:Ai:Ollama": {
    "BaseUrl": "http://localhost:11434",
    "DefaultModel": "llama3.1:8b"
  }
}

// NEW (recommended):
{
  "Koan:Ai:Sources:ollama": {
    "Provider": "ollama",
    "ConnectionString": "http://localhost:11434",
    "Priority": 50,
    "Capabilities": {
      "Chat": { "Model": "llama3.1:8b" },
      "Embedding": { "Model": "all-minilm:latest" }
    }
  }
}
```

---

## Risk Assessment

### High Risk
- Changing adapter lifecycle affects all AI consumers
- Connection pooling must maintain thread safety
- Breaking changes if not backward compatible

### Medium Risk
- Performance impact of source registry lookups (mitigated by ConcurrentDictionary)
- Memory usage from HttpClient pool (mitigated by URL-based pooling)

### Low Risk
- Configuration format changes (backward compatible via legacy path)
- Source naming conflicts (validated at registration)

---

## Success Metrics

- ✅ ONE adapter instance registered per provider type
- ✅ Router successfully resolves source monikers to URLs
- ✅ No "No adapter found for source" warnings in logs
- ✅ Embedding requests complete successfully
- ✅ Zero-config discovery works without explicit configuration
- ✅ Explicit source routing: `Ai.Chat(source: "enterprise")` works

---

## Related Documents

- ADR-0014: AI Modernization with Multi-Source Discovery
- `docs/architecture/ai-routing.md` (to be created)
- `src/Koan.AI/README.md` (needs update)

---

## Implementation Timeline

**Phase 1-3 (Critical Path):** 2-3 days
**Phase 4-5 (Cleanup):** 1 day
**Testing & Documentation:** 1 day

**Total Estimated Time:** 4-5 days

---

## Conclusion

The current architecture violates the Adapter (HOW) vs Source (WHERE) separation by creating multiple adapter instances per URL. The fix requires:

1. **Stop** creating multiple adapters in discovery loop
2. **Enable** adapter to resolve source monikers to URLs at runtime
3. **Use** `source.Provider` field for adapter lookup instead of URL pattern matching

This aligns with ADR-0014 principles and enables proper multi-source routing with fast hashmap resolution.
