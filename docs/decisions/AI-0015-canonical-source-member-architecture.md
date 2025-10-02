# AI-0015: Canonical Source-Member Architecture

**Status:** Accepted (Canonical) - **PHASE 3 COMPLETE**
**Date:** 2025-10-01
**Supersedes:** AI-0014, AI-SOURCE-ROUTING-FIX, AI-SOURCE-MEMBER-IMPLEMENTATION-PLAN
**Implementation Started:** 2025-10-01

---

## 🔄 IMPLEMENTATION STATUS

**Current Phase:** CORE IMPLEMENTATION COMPLETE (Phases 1-3) ✅

### ✅ Completed (WORKING END-TO-END)
- [x] **Phase 1: Data Models**
  - Created `AiMemberDefinition` (endpoint with ConnectionString, Order, health state)
  - Rebuilt `AiSourceDefinition` (collection with Members list, Priority, Policy)
  - Added `CircuitBreakerConfig`, `SourceHealthState`, `MemberHealthState`
  - Deprecated `AiGroupDefinition` (marked obsolete, will remove)
  - Created `LegacyAiSourceDefinition` and `LegacyAiGroupDefinition` for migration

- [x] **Phase 2: Registry Foundation**
  - Updated `IAiSourceRegistry` interface (added `GetSourcesWithCapability`)
  - Deprecated `IAiGroupRegistry` (marked obsolete)
  - Rebuilt `AiSourceRegistry`:
    - ✅ NO implicit "Default" source creation (ADR requirement)
    - ✅ Source name collision detection with fail-fast
    - ✅ Validation: source names cannot contain `::`
    - ✅ Legacy `Koan:Ai:Ollama` config support (backward compat)
    - ✅ Explicit source configuration from `Koan:Ai:Sources`

- [x] **Phase 3: Router & Adapter** (COMPLETE - TESTED)
  - Rebuilt `DefaultAiRouter` (543 → 249 lines, 54% reduction)
  - Priority-based source election working
  - Member selection (Fallback policy by Order)
  - Member pinning (`source::member` syntax) working
  - Fail-fast error handling with clear messages
  - **Simplified logging:** Single Info/Error line per request with full context
  - OllamaAdapter singleton pattern with URL injection
  - Request models updated (InternalConnectionString)

**✅ VERIFIED IN S5.RECS:**
```
AI route OK: ollama/qwen3-embedding:8b via ollama:ollama::host (embed 1 inputs)
```
**Log Format:**
- Success: `AI route OK: {Adapter}/{Model} via {Source}:{Member} [context]`
- Failure: `AI route FAIL: {Adapter}/{Model} via {Source}:{Member} - {Error}`

Embedding requests completing successfully - NO MORE EMPTY "Default" SOURCE ERRORS!

### ⏳ Pending (Advanced Features - Not Blocking)
- [ ] **Phase 4: Advanced Policies**
  - RoundRobin policy (member-based selection with rotation)
  - WeightedRoundRobin policy (member-based with weights)
  - Policy factory pattern
  - Advanced health-aware member selection

- [ ] **Phase 5: Health Monitoring**
  - Member-level circuit breakers
  - Source health aggregation
  - Update `AiSourceHealthMonitor` background service

- [ ] **Phase 6: Adapter Updates**
  - `OllamaAdapter`: Singleton pattern with HttpClient pool by URL
  - Remove stateful BaseAddress
  - Accept URL via `request.InternalConnectionString`

- [ ] **Phase 7: Boot Report**
  - Hierarchical format (sources → members → capabilities)
  - Health state display

- [ ] **Phase 8: Testing**
  - S5.Recs embedding functionality verification
  - Integration tests
  - Unit tests for election and member selection

### 🐛 Known Issues Being Fixed
- **Root Cause Identified:** Empty "Default" source created before discovery → no capabilities → routing failure
- **Fix Applied:** Registry no longer creates implicit "Default" source
- **Discovery Fix Pending:** Create proper "ollama" source with members

---

## 🔴 AUTHORITATIVE SPECIFICATION

**This ADR is the single source of truth for AI routing architecture.**

All previous documentation (ADR-0014, AI-SOURCE-ROUTING-FIX, etc.) contained terminology inversions and inconsistencies. This document consolidates and corrects all previous decisions.

---

## Executive Summary

**Koan.AI uses a two-level hierarchy:**
- **Source** = Collection of members with priority, policy, and routing rules
- **Member** = Individual endpoint (URL) that serves AI requests

**Example:**
```
Source "ollama" (priority 50, policy: Fallback)
  ├─ Member "ollama::host" → http://host.docker.internal:11434
  └─ Member "ollama::container" → http://localhost:11434

Source "enterprise" (priority 100, policy: RoundRobin)
  ├─ Member "enterprise::ollama-1" → http://ollama1.corp:11434
  └─ Member "enterprise::ollama-2" → http://ollama2.corp:11434
```

**Routing:**
```csharp
Ai.Chat("Hello")                                    // Elects "enterprise" (priority 100)
Ai.Chat(new AiChatOptions { Source = "ollama" })   // Uses "ollama" source, policy selects member
Ai.Chat(new AiChatOptions { Source = "ollama::host" }) // Pins to specific member
```

---

## Terminology (Canonical)

### Source
**Definition:** A named collection of members with shared priority, policy, and capabilities.

**Characteristics:**
- Has unique name (e.g., "ollama", "enterprise")
- Has priority (for source election)
- Has policy (Fallback, RoundRobin, WeightedRoundRobin)
- Contains 1+ members
- May be auto-created by adapter or explicitly configured

**Priority Scale:**
- 100+ = Explicit user configuration (highest)
- 50 = Adapter-provided auto-discovery (default)
- 0-49 = Degraded/fallback sources

### Member
**Definition:** An individual AI service endpoint within a source.

**Characteristics:**
- Has unique name with `source::identifier` pattern (e.g., "ollama::host")
- Has connection string (URL)
- Has capabilities (discovered or configured)
- Belongs to exactly one source

**Naming Convention:**
- Format: `{sourceName}::{memberIdentifier}`
- Examples: `ollama::host`, `enterprise::ollama-1`, `ollama::gpu`
- Source names MUST NOT contain `::`
- Member names MUST contain `::`

### Adapter
**Definition:** Protocol implementation (singleton) that knows HOW to communicate with a provider.

**Characteristics:**
- Registered once per provider type
- ID matches provider name (e.g., "ollama")
- Stateless with respect to endpoints
- Uses HttpClient pool keyed by URL

---

## Core Concepts

### 1. Source Election (Priority-Based)

When no source is specified, router elects highest-priority source with required capability:

```
Request: Ai.Chat("Hello")

Election:
1. Get all sources with "Chat" capability
2. Order by priority descending
3. Select first: "enterprise" (priority 100)
4. Apply policy to select member
```

### 2. Policy-Based Member Selection

Within a source, policy determines which member handles the request:

**Fallback Policy:**
```
Members: ["ollama::host", "ollama::container"]
1. Try "ollama::host" (first in order)
2. If circuit open/unhealthy, try "ollama::container"
3. If all fail, fail request
```

**RoundRobin Policy:**
```
Members: ["enterprise::ollama-1", "enterprise::ollama-2"]
1. Rotate through members on each request
2. Skip unhealthy members
```

**WeightedRoundRobin Policy:**
```
Members with weights: [("gpu", 3), ("cpu", 1)]
1. Select "gpu" 75% of time, "cpu" 25%
2. Implements weighted distribution
```

### 3. Member Pinning (Bypass Policy)

Direct member reference bypasses policy selection:

```csharp
Ai.Chat(new AiChatOptions { Source = "ollama::host" })

Resolution:
1. Parse: source="ollama", memberIdentifier="host"
2. Lookup source "ollama"
3. Find member "ollama::host" in source.Members
4. Pin to this member (no policy, no fallback to other members)
5. If member unhealthy, fail fast
```

### 4. Adapter Singleton Pattern

One adapter instance per provider, connection resolved per request:

```csharp
// Singleton adapter
public class OllamaAdapter : IAiAdapter
{
    public string Id => "ollama";
    private readonly ConcurrentDictionary<string, HttpClient> _clientPool = new();

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct)
    {
        // Router resolves source hint to member URL
        var memberUrl = request.InternalConnectionString; // Set by router
        var client = GetOrCreateClient(memberUrl);
        // Execute request...
    }
}
```

**Adapter does NOT:**
- Know about sources or members
- Maintain source-specific state
- Manage routing logic

**Router responsibilities:**
- Resolve source hint → member URL
- Apply policy
- Handle health checks
- Inject resolved URL into request

---

## Configuration Schema

### Level 0: Zero Config (Full Auto-Discovery)

```json
{
  // No AI configuration
}
```

**Behavior:**
- Ollama adapter runs discovery
- Checks: `host.docker.internal:11434`, `ollama:11434`, `localhost:11434`
- Creates source "ollama" (priority 50)
- Adds discovered members: `ollama::host`, `ollama::container`, etc.
- Introspects capabilities via `/api/tags`

**Result:**
```
Source "ollama" (priority 50, policy: Fallback)
  ├─ ollama::host → http://host.docker.internal:11434
  │   └─ Capabilities: Chat→llama3.2, Embedding→nomic-embed-text
  └─ ollama::container → http://localhost:11434
      └─ Capabilities: Chat→llama3.2, Embedding→nomic-embed-text
```

---

### Level 1: Model Overlay

```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "DefaultModel": "llama3.2"
      }
    }
  }
}
```

**Behavior:**
- Discovery runs (no `Urls` specified)
- DefaultModel overlays onto all capabilities
- All discovered members get `Chat→llama3.2`, `Embedding→llama3.2`

---

### Level 2: Capability-Specific Models

```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "DefaultModel": "llama3.2",
        "Capabilities": {
          "Chat": { "Model": "llama3.2:70b" },
          "Embedding": { "Model": "nomic-embed-text" }
        }
      }
    }
  }
}
```

**Behavior:**
- Discovery runs
- Capabilities map overrides DefaultModel for specific capabilities
- Members get: `Chat→llama3.2:70b`, `Embedding→nomic-embed-text`
- If capability not in map (e.g., Vision), uses DefaultModel

**Precedence:** `Capabilities[X]` > `DefaultModel` > introspected

---

### Level 3: Additional Members (Extend Discovery)

```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "DefaultModel": "llama3.2",
        "AdditionalUrls": [
          "http://gpu-server:11434",
          "http://backup-server:11434"
        ]
      }
    }
  }
}
```

**Behavior:**
- ✅ Discovery runs (checks host, container, localhost)
- ✅ Discovered members added to source
- ✅ AdditionalUrls members added to source
- Members auto-named: `ollama::additional-1`, `ollama::additional-2`

**Result:**
```
Source "ollama" (priority 50)
  ├─ ollama::host → http://host.docker.internal:11434 (discovered)
  ├─ ollama::container → http://localhost:11434 (discovered)
  ├─ ollama::additional-1 → http://gpu-server:11434 (user config)
  └─ ollama::additional-2 → http://backup-server:11434 (user config)
```

---

### Level 4: Explicit Members Only (Disable Discovery)

```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "DefaultModel": "llama3.2",
        "Urls": [
          "http://prod-ollama-1:11434",
          "http://prod-ollama-2:11434"
        ]
      }
    }
  }
}
```

**Behavior:**
- ❌ Discovery SKIPPED (presence of `Urls` disables discovery)
- ✅ Only user-specified URLs become members
- Members auto-named: `ollama::explicit-1`, `ollama::explicit-2`

**Result:**
```
Source "ollama" (priority 50)
  ├─ ollama::explicit-1 → http://prod-ollama-1:11434
  └─ ollama::explicit-2 → http://prod-ollama-2:11434
```

**Semantic:**
- `AdditionalUrls` = discovery + overlay
- `Urls` = explicit only, no discovery

---

### Level 5: Explicit Source Definition

```json
{
  "Koan": {
    "Ai": {
      "Sources": {
        "enterprise": {
          "Priority": 100,
          "Policy": "RoundRobin",
          "Ollama": {
            "Urls": [
              "http://ollama1.corp:11434",
              "http://ollama2.corp:11434"
            ],
            "Capabilities": {
              "Chat": { "Model": "llama3.2:70b" }
            }
          }
        }
      }
    }
  }
}
```

**Behavior:**
- Creates source "enterprise" (priority 100)
- Uses Ollama adapter (inferred from provider config key)
- Members: `enterprise::explicit-1`, `enterprise::explicit-2`
- Policy: RoundRobin

**Result:**
```
Source "enterprise" (priority 100, policy: RoundRobin)
  ├─ enterprise::explicit-1 → http://ollama1.corp:11434
  │   └─ Capabilities: Chat→llama3.2:70b
  └─ enterprise::explicit-2 → http://ollama2.corp:11434
      └─ Capabilities: Chat→llama3.2:70b
```

---

### Level 6: Multi-Source Configuration

```json
{
  "Koan": {
    "Ai": {
      "Policy": "Fallback",
      "Ollama": {
        "Policy": "RoundRobin",
        "DefaultModel": "llama3.2"
      },
      "Sources": {
        "enterprise": {
          "Priority": 100,
          "Policy": "Fallback",
          "Ollama": {
            "Urls": ["http://ollama1:11434", "http://ollama2:11434"]
          }
        }
      }
    }
  }
}
```

**Result:**
```
Source "enterprise" (priority 100, policy: Fallback)
  ├─ enterprise::explicit-1 → http://ollama1:11434
  └─ enterprise::explicit-2 → http://ollama2:11434

Source "ollama" (priority 50, policy: RoundRobin)
  ├─ ollama::host → http://host.docker.internal:11434
  └─ ollama::container → http://localhost:11434
```

**Policy Precedence (least → most specific):**
1. `Koan:Ai:Policy` (global default)
2. `Koan:Ai:{adapter}:Policy` (adapter-level override)
3. `Koan:Ai:Sources:{source}:Policy` (source-specific override)

---

## Routing Resolution Logic

### Source Resolution Algorithm

```csharp
AiSourceDefinition ResolveSource(string? sourceHint, string capability)
{
    // 1. Explicit source or member hint provided
    if (!string.IsNullOrWhiteSpace(sourceHint))
    {
        // Try direct source lookup
        var source = _sourceRegistry.GetSource(sourceHint);
        if (source != null)
            return source;

        // Parse member reference (contains ::)
        if (sourceHint.Contains("::"))
        {
            var sourceName = sourceHint.Split("::")[0];
            source = _sourceRegistry.GetSource(sourceName);
            if (source != null)
                return source;

            // Member not found - FAIL FAST
            throw new InvalidOperationException(
                $"Member '{sourceHint}' not found. Available members in source '{sourceName}': " +
                string.Join(", ", source.Members.Select(m => m.Name)));
        }

        // Source not found - FAIL FAST
        throw new InvalidOperationException(
            $"Source '{sourceHint}' not found. Available sources: " +
            string.Join(", ", _sourceRegistry.GetSourceNames()));
    }

    // 2. No hint - elect by priority
    var candidates = _sourceRegistry.GetAllSources()
        .Where(s => s.Capabilities.ContainsKey(capability))
        .OrderByDescending(s => s.Priority)
        .ToList();

    if (candidates.Count == 0)
    {
        throw new InvalidOperationException(
            $"No source found with capability '{capability}'. " +
            "Configure a source or enable auto-discovery.");
    }

    var elected = candidates.First();

    _logger?.LogDebug(
        "Elected source '{Source}' (priority {Priority}) for capability '{Capability}'",
        elected.Name, elected.Priority, capability);

    return elected;
}
```

### Member Selection Algorithm

```csharp
AiMemberDefinition SelectMember(AiSourceDefinition source, string? sourceHint)
{
    // 1. Check for member pinning (sourceHint contains ::)
    if (!string.IsNullOrWhiteSpace(sourceHint) && sourceHint.Contains("::"))
    {
        var pinnedMember = source.Members.FirstOrDefault(m =>
            string.Equals(m.Name, sourceHint, StringComparison.OrdinalIgnoreCase));

        if (pinnedMember == null)
        {
            // FAIL FAST - member specified but not found
            throw new InvalidOperationException(
                $"Member '{sourceHint}' not found in source '{source.Name}'. " +
                $"Available members: {string.Join(", ", source.Members.Select(m => m.Name))}");
        }

        _logger?.LogDebug(
            "Pinned to member '{Member}' (policy bypassed)",
            pinnedMember.Name);

        return pinnedMember;
    }

    // 2. Apply policy to select member
    var policy = _policyFactory.CreatePolicy(source.Policy);
    var selectedMember = policy.SelectMember(source.Members, _healthRegistry);

    if (selectedMember == null)
    {
        throw new InvalidOperationException(
            $"No healthy members available in source '{source.Name}'. " +
            $"Total members: {source.Members.Count}");
    }

    _logger?.LogDebug(
        "Policy '{Policy}' selected member '{Member}' from source '{Source}'",
        source.Policy, selectedMember.Name, source.Name);

    return selectedMember;
}
```

### Adapter Resolution

```csharp
IAiAdapter ResolveAdapter(AiSourceDefinition source)
{
    if (string.IsNullOrWhiteSpace(source.Provider))
    {
        throw new InvalidOperationException(
            $"Source '{source.Name}' has no provider configured");
    }

    var adapter = _adapterRegistry.Get(source.Provider);

    if (adapter == null)
    {
        throw new InvalidOperationException(
            $"No adapter found for provider '{source.Provider}'. " +
            $"Available adapters: {string.Join(", ", _adapterRegistry.All.Select(a => a.Id))}");
    }

    return adapter;
}
```

### Full Request Flow

```csharp
public async Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct)
{
    var sourceHint = request.Route?.AdapterId; // Will rename to SourceHint
    var capability = "Chat";

    // 1. Resolve source (election or explicit)
    var source = ResolveSource(sourceHint, capability);

    // 2. Select member (policy or pinned)
    var member = SelectMember(source, sourceHint);

    // 3. Get adapter for provider
    var adapter = ResolveAdapter(source);

    // 4. Inject member URL into request
    request.InternalConnectionString = member.ConnectionString;

    // 5. Get effective model
    var effectiveModel = request.Model
        ?? member.Capabilities?.GetValueOrDefault(capability)?.Model;

    // 6. Execute request with logging
    try
    {
        var response = await adapter.ChatAsync(request, ct);

        _logger?.LogInformation(
            "AI route OK: {Adapter}/{Model} via {Source}:{Member}",
            adapter.Id, effectiveModel ?? "(default)", source.Name, member.Name);

        return response;
    }
    catch (Exception ex)
    {
        _logger?.LogError(
            "AI route FAIL: {Adapter}/{Model} via {Source}:{Member} - {Error}",
            adapter.Id, effectiveModel ?? "(default)", source.Name, member.Name, ex.Message);
        throw;
    }
}
```

---

## Capability Introspection

### Discovery-Time Introspection

**When:** During auto-discovery for discovered members

**How:**
```csharp
var capabilities = await IntrospectCapabilities(memberUrl, cancellationToken);
```

**Calls:** `GET {memberUrl}/api/tags` (Ollama-specific)

**Caching:** Results cached to persistent storage (container or mounted volume)

---

### Lazy Introspection with Caching

**When:** First request to a member created from explicit config (no introspection at startup)

**Cache Location:**
```
/app/cache/ai-introspection/{hash(memberUrl)}.json
```

**Cache Structure:**
```json
{
  "url": "http://ollama1:11434",
  "introspectedAt": "2025-10-01T12:00:00Z",
  "capabilities": {
    "Chat": { "Model": "llama3.2" },
    "Embedding": { "Model": "nomic-embed-text" }
  }
}
```

**Cache Invalidation:**
- TTL: 24 hours
- Manual: Delete cache file
- On startup: Check if cache exists, load if valid

**Benefits:**
- Fast startup (no blocking introspection)
- Persistent across container restarts
- Automatic discovery of new models

---

## Health Monitoring

### Source Health States

**Healthy:** All members in source are healthy
**Degraded:** At least one member healthy, but not all
**Unhealthy:** No members healthy

```csharp
public enum SourceHealthState
{
    Healthy,    // All members healthy
    Degraded,   // Some members unhealthy
    Unhealthy   // All members unhealthy
}
```

### Member Health States

**Healthy:** Circuit closed, passing health checks
**Unhealthy:** Circuit open due to failures
**Unknown:** Not yet probed

### Health Registry

```csharp
public interface ISourceHealthRegistry
{
    SourceHealthState GetSourceHealth(string sourceName);
    MemberHealthState GetMemberHealth(string memberName);
    void RecordSuccess(string memberName);
    void RecordFailure(string memberName);
}
```

### Circuit Breaker (Per-Member)

**Configuration:**
```json
{
  "Koan": {
    "Ai": {
      "CircuitBreaker": {
        "FailureThreshold": 3,
        "BreakDurationSeconds": 30,
        "SuccessThreshold": 2
      }
    }
  }
}
```

**Behavior:**
1. After 3 consecutive failures → Circuit opens
2. Member marked unhealthy for 30 seconds
3. After 30 seconds → Circuit enters half-open
4. If next 2 requests succeed → Circuit closes
5. If any request fails in half-open → Circuit re-opens

---

## Error Handling & Fail-Fast Semantics

### Member Not Found (Pinning)

```csharp
Ai.Chat(new AiChatOptions { Source = "ollama::nonexistent" })
```

**Error:**
```
InvalidOperationException: Member 'ollama::nonexistent' not found in source 'ollama'.
Available members: ollama::host, ollama::container
```

**Fail fast:** Immediate exception, no retry, no fallback

---

### Source Not Found

```csharp
Ai.Chat(new AiChatOptions { Source = "nonexistent" })
```

**Error:**
```
InvalidOperationException: Source 'nonexistent' not found.
Available sources: ollama, enterprise
```

**Fail fast:** Immediate exception

---

### Wrong Model for Capability

```json
{
  "Capabilities": {
    "Embedding": { "Model": "llama3.2" }  // Wrong: llama3.2 is chat model
  }
}
```

**Behavior:**
- Always honor user selection
- Attempt request with specified model
- Adapter/Ollama returns error (e.g., model doesn't support embeddings)
- Fail with clear error from adapter

**Error (from Ollama):**
```
OllamaException: Model 'llama3.2' does not support embedding capability.
Suggested models: nomic-embed-text, all-minilm
```

**Rationale:** User may know better than framework; don't second-guess explicit config.

---

### Source Name Collision

**Scenario:**
```json
{
  "Koan": {
    "Ai": {
      "Ollama": { ... },  // Creates source "ollama"
      "Sources": {
        "ollama": { ... }  // Collision!
      }
    }
  }
}
```

**Behavior:** **Fail fast on startup**

**Error:**
```
InvalidOperationException: Source name collision detected: 'ollama' already registered by adapter 'Koan.AI.Connector.Ollama'.
Use a different name for explicit source (e.g., 'ollama-custom') or remove Koan:Ai:Ollama configuration.
```

---

## Boot Report

### Format (Hierarchical)

```
┌─ Koan FRAMEWORK v0.3.0 ─────────────────
│
│ AI Sources (2)
│
│ ├─ enterprise (priority 100, policy: RoundRobin) ⭐ HIGHEST PRIORITY
│ │   ├─ Provider: ollama
│ │   ├─ Origin: explicit-config
│ │   ├─ Health: Healthy (2/2 members)
│ │   ├─ Members (2):
│ │   │   ├─ enterprise::explicit-1 → http://ollama1.corp:11434 [Healthy]
│ │   │   └─ enterprise::explicit-2 → http://ollama2.corp:11434 [Healthy]
│ │   └─ Capabilities:
│ │       └─ Chat → llama3.2:70b
│ │
│ └─ ollama (priority 50, policy: Fallback)
│     ├─ Provider: ollama
│     ├─ Origin: auto-discovery
│     ├─ Health: Degraded (1/2 members)
│     ├─ Members (2):
│     │   ├─ ollama::host → http://host.docker.internal:11434 [Healthy]
│     │   └─ ollama::container → http://localhost:11434 [Unhealthy - Circuit Open]
│     └─ Capabilities:
│         ├─ Chat → llama3.2
│         └─ Embedding → nomic-embed-text
│
│ Routing Behavior:
│   ├─ No source specified: Elects 'enterprise' (highest priority)
│   ├─ Source "ollama": Uses Fallback policy → tries ollama::host
│   └─ Source "ollama::host": Pins to specific member (no policy)
│
└─────────────────────────────────────────
```

---

## Data Models

### AiSourceDefinition (Collection)

```csharp
public record AiSourceDefinition
{
    /// <summary>Source name (e.g., "ollama", "enterprise")</summary>
    public required string Name { get; init; }

    /// <summary>Adapter provider name (e.g., "ollama", "openai")</summary>
    public required string Provider { get; init; }

    /// <summary>Priority for source election (higher = preferred)</summary>
    public int Priority { get; init; } = 50;

    /// <summary>Member selection policy (Fallback, RoundRobin, WeightedRoundRobin)</summary>
    public string Policy { get; init; } = "Fallback";

    /// <summary>Members (endpoints) in this source</summary>
    public required List<AiMemberDefinition> Members { get; init; }

    /// <summary>Shared capabilities for all members (can be overridden per-member)</summary>
    public Dictionary<string, AiCapabilityConfig> Capabilities { get; init; } = new();

    /// <summary>Circuit breaker configuration for members</summary>
    public CircuitBreakerConfig? CircuitBreaker { get; init; }

    /// <summary>Origin: "auto-discovery", "explicit-config", "legacy-config"</summary>
    public string Origin { get; init; } = "explicit-config";

    /// <summary>Whether source was auto-discovered by adapter</summary>
    public bool IsAutoDiscovered { get; init; }
}
```

### AiMemberDefinition (Endpoint)

```csharp
public record AiMemberDefinition
{
    /// <summary>Member name with source::identifier pattern (e.g., "ollama::host")</summary>
    public required string Name { get; init; }

    /// <summary>Connection string (URL) for this member</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Order for fallback policy (0 = first tried)</summary>
    public int Order { get; init; }

    /// <summary>Weight for weighted round-robin (default 1)</summary>
    public int Weight { get; init; } = 1;

    /// <summary>Member-specific capabilities (overrides source capabilities)</summary>
    public Dictionary<string, AiCapabilityConfig>? Capabilities { get; init; }

    /// <summary>Whether member was auto-discovered</summary>
    public bool IsAutoDiscovered { get; init; }

    /// <summary>Origin: "discovered", "config-urls", "config-additional-urls"</summary>
    public string Origin { get; init; } = "discovered";
}
```

### AiCapabilityConfig

```csharp
public record AiCapabilityConfig
{
    /// <summary>Model name for this capability</summary>
    public required string Model { get; init; }

    /// <summary>Capability-specific options (temperature, max_tokens, etc.)</summary>
    public Dictionary<string, object>? Options { get; init; }

    /// <summary>Whether to auto-download missing model (Ollama only)</summary>
    public bool AutoDownload { get; init; } = true;
}
```

### CircuitBreakerConfig

```csharp
public record CircuitBreakerConfig
{
    public int FailureThreshold { get; init; } = 3;
    public int BreakDurationSeconds { get; init; } = 30;
    public int SuccessThreshold { get; init; } = 2;
}
```

---

## Discovery Implementation

### OllamaDiscoveryService.StartAsync()

```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    var ollamaConfig = _cfg.GetSection("Koan:Ai:Ollama");

    // 1. Check for explicit Urls (disables discovery)
    var explicitUrls = ollamaConfig.GetSection("Urls").Get<string[]>();
    var additionalUrls = ollamaConfig.GetSection("AdditionalUrls").Get<string[]>();
    var defaultModel = ollamaConfig["DefaultModel"];

    // 2. Validate configuration
    ValidateConfiguration(explicitUrls, additionalUrls);

    // 3. Build member list
    List<(string Name, string Url, string Origin)> members = new();

    if (explicitUrls?.Length > 0)
    {
        // Explicit mode: NO discovery
        _logger.LogInformation(
            "Ollama configured with explicit URLs - skipping auto-discovery");

        members.AddRange(explicitUrls.Select((url, idx) =>
            ($"ollama::explicit-{idx + 1}", url, "config-urls")));
    }
    else
    {
        // Discovery mode
        _logger.LogInformation("Starting Ollama auto-discovery");

        var discovered = await DiscoverOllamaInstances(cancellationToken);
        members.AddRange(discovered.Select(d => (d.Name, d.Url, "discovered")));

        // Add additional URLs if specified
        if (additionalUrls?.Length > 0)
        {
            _logger.LogInformation(
                "Adding {Count} additional Ollama URLs from configuration",
                additionalUrls.Length);

            members.AddRange(additionalUrls.Select((url, idx) =>
                ($"ollama::additional-{idx + 1}", url, "config-additional-urls")));
        }
    }

    if (members.Count == 0)
    {
        _logger.LogWarning("No Ollama instances found or configured");
        return;
    }

    // 4. Create source with members
    await CreateSource("ollama", members, defaultModel, ollamaConfig, cancellationToken);
}

private async Task<List<(string Name, string Url)>> DiscoverOllamaInstances(CancellationToken ct)
{
    var candidates = new[]
    {
        ("ollama::host", "http://host.docker.internal:11434"),
        ("ollama::linked", "http://ollama:11434"),
        ("ollama::container", "http://localhost:11434")
    };

    var discovered = new List<(string Name, string Url)>();

    foreach (var (name, url) in candidates)
    {
        if (await IsHealthy(url, ct))
        {
            discovered.Add((name, url));
            _logger.LogDebug("Discovered Ollama instance: {Name} → {Url}", name, url);
        }
    }

    return discovered;
}
```

### Capability Introspection (Lazy + Cached)

```csharp
public async Task<Dictionary<string, AiCapabilityConfig>> GetCapabilitiesAsync(
    string memberUrl,
    string? defaultModel,
    CancellationToken ct)
{
    // 1. Check cache
    var cacheKey = ComputeHash(memberUrl);
    var cachePath = Path.Combine("/app/cache/ai-introspection", $"{cacheKey}.json");

    if (File.Exists(cachePath))
    {
        var cacheJson = await File.ReadAllTextAsync(cachePath, ct);
        var cached = JsonSerializer.Deserialize<CachedCapabilities>(cacheJson);

        // Check if cache is still valid (24 hours)
        if (cached != null && (DateTime.UtcNow - cached.IntrospectedAt).TotalHours < 24)
        {
            _logger.LogDebug(
                "Using cached capabilities for {Url} (age: {Age:F1}h)",
                memberUrl,
                (DateTime.UtcNow - cached.IntrospectedAt).TotalHours);

            return cached.Capabilities;
        }
    }

    // 2. Introspect via /api/tags
    var capabilities = await IntrospectOllamaCapabilities(memberUrl, defaultModel, ct);

    // 3. Cache result
    var cacheData = new CachedCapabilities
    {
        Url = memberUrl,
        IntrospectedAt = DateTime.UtcNow,
        Capabilities = capabilities
    };

    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
    await File.WriteAllTextAsync(
        cachePath,
        JsonSerializer.Serialize(cacheData),
        ct);

    return capabilities;
}

private record CachedCapabilities
{
    public string Url { get; init; } = "";
    public DateTime IntrospectedAt { get; init; }
    public Dictionary<string, AiCapabilityConfig> Capabilities { get; init; } = new();
}
```

---

## Policy Precedence

**Configuration layers (least → most specific):**

1. **Framework default:** `"Fallback"`
2. **Global override:** `Koan:Ai:Policy`
3. **Adapter override:** `Koan:Ai:Ollama:Policy`
4. **Source override:** `Koan:Ai:Sources:enterprise:Policy`

**Example:**
```json
{
  "Koan": {
    "Ai": {
      "Policy": "RoundRobin",           // Global: applies to all sources
      "Ollama": {
        "Policy": "Fallback"             // Adapter-level: overrides global for "ollama" source
      },
      "Sources": {
        "enterprise": {
          "Policy": "WeightedRoundRobin" // Source-level: overrides everything for "enterprise"
        }
      }
    }
  }
}
```

**Result:**
- Source "enterprise": `WeightedRoundRobin` (source-specific)
- Source "ollama": `Fallback` (adapter-level)
- Any other source: `RoundRobin` (global)

---

## Usage Examples

### Example 1: Zero Config

```csharp
// No configuration needed - auto-discovery runs
var response = await Ai.Chat("What is quantum computing?");
```

**Resolution:**
1. No source hint → elect by priority
2. Source "ollama" elected (only source, priority 50)
3. Policy "Fallback" selects member "ollama::host"
4. Adapter "ollama" executes request at `http://host.docker.internal:11434`

---

### Example 2: Source Selection

```csharp
var response = await Ai.Chat(new AiChatOptions
{
    Message = "Analyze this data",
    Source = "enterprise"
});
```

**Resolution:**
1. Source hint "enterprise" → direct lookup
2. Source "enterprise" found (priority 100)
3. Policy "RoundRobin" rotates between members
4. Selected: "enterprise::explicit-2"
5. Adapter "ollama" executes at `http://ollama2.corp:11434`

---

### Example 3: Member Pinning

```csharp
using (Ai.Context(source: "ollama::host"))
{
    var embedding = await Ai.Embed("Test document");
}
```

**Resolution:**
1. Source hint "ollama::host" → parse as member
2. Parse: source="ollama", member="host"
3. Lookup source "ollama" → found
4. Find member "ollama::host" → found
5. Pin to this member (policy bypassed)
6. Adapter "ollama" executes at `http://host.docker.internal:11434`

---

### Example 4: Election with Multiple Sources

```csharp
// Two sources configured:
// - "enterprise" (priority 100)
// - "ollama" (priority 50)

var response = await Ai.Chat("Hello");
```

**Resolution:**
1. No hint → elect by priority
2. Candidates: ["enterprise" (100), "ollama" (50)]
3. Elect "enterprise" (highest priority)
4. Policy selects member
5. Execute request

---

### Example 5: Capability-Specific Routing

```csharp
// enterprise: Has Chat only
// ollama: Has Chat + Embedding

var chatResponse = await Ai.Chat("Hello");       // Uses "enterprise" (priority 100)
var embedding = await Ai.Embed("Document text"); // Uses "ollama" (only source with Embedding)
```

**Resolution:**
- Chat: Both sources have capability → elect "enterprise" (priority)
- Embedding: Only "ollama" has capability → use "ollama"

---

## Migration from Previous Implementation

### Code Changes

**Before (Incorrect):**
```csharp
// Registry creates empty "Default" source
RegisterSource(new AiSourceDefinition { Name = "Default", Capabilities = {} });

// Discovery creates "Group" with "Sources"
_groupRegistry.RegisterGroup("ollama-auto");
_sourceRegistry.RegisterSource("ollama-auto-host");

// Router tries URL pattern matching
if (adapter.Id.Contains($"{host}:{port}"))
    return adapter;
```

**After (Correct):**
```csharp
// No "Default" source - use priority election
var elected = _sourceRegistry.GetAllSources()
    .Where(s => s.Capabilities.ContainsKey(capability))
    .OrderByDescending(s => s.Priority)
    .First();

// Discovery creates "Source" with "Members"
var source = new AiSourceDefinition
{
    Name = "ollama",
    Members = [
        new AiMemberDefinition { Name = "ollama::host", ... },
        new AiMemberDefinition { Name = "ollama::container", ... }
    ]
};

// Router uses provider-based lookup
var adapter = _adapterRegistry.Get(source.Provider);
```

### Configuration Migration

**Before:**
```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "DefaultModel": "llama3.2"
      }
    }
  }
}
```

**After:** (Same - backward compatible)
```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "DefaultModel": "llama3.2"
      }
    }
  }
}
```

**No breaking changes for basic configuration.**

---

## Implementation Checklist

### Phase 1: Data Models (Week 1)

- [ ] Create `AiSourceDefinition` (new - collection)
- [ ] Create `AiMemberDefinition` (new - endpoint)
- [ ] Rename old `AiSourceDefinition` → `LegacyAiSourceDefinition` (temp)
- [ ] Rename old `AiGroupDefinition` → `LegacyAiGroupDefinition` (temp)
- [ ] Update `IAiSourceRegistry` interface
- [ ] Implement new `AiSourceRegistry`

### Phase 2: Discovery (Week 1-2)

- [ ] Update `OllamaDiscoveryService` to create source with members
- [ ] Implement `Urls` vs `AdditionalUrls` semantics
- [ ] Implement lazy capability introspection with caching
- [ ] Remove "Default" source creation from registry
- [ ] Implement source name collision detection

### Phase 3: Router (Week 2)

- [ ] Implement `ResolveSource()` with election logic
- [ ] Implement `SelectMember()` with policy + pinning
- [ ] Update `ResolveAdapter()` to use source.Provider
- [ ] Remove URL pattern matching logic
- [ ] Implement fail-fast error handling
- [ ] Add one-liner structured logging

### Phase 4: Policies (Week 2-3)

- [ ] Implement policy precedence (global → adapter → source)
- [ ] Update `FallbackPolicy` to use members
- [ ] Update `RoundRobinPolicy` to use members
- [ ] Create `WeightedRoundRobinPolicy`
- [ ] Integrate health registry with member selection

### Phase 5: Health Monitoring (Week 3)

- [ ] Update `ISourceHealthRegistry` for member-level tracking
- [ ] Implement source health aggregation (Healthy/Degraded/Unhealthy)
- [ ] Implement per-member circuit breakers
- [ ] Update `AiSourceHealthMonitor` background service

### Phase 6: Adapter Updates (Week 3)

- [ ] Update `OllamaAdapter` to use HttpClient pool
- [ ] Accept URL via `request.InternalConnectionString`
- [ ] Remove stateful BaseAddress from adapter
- [ ] Update other adapters (OpenAI, Anthropic) if needed

### Phase 7: Boot Report (Week 4)

- [ ] Implement hierarchical boot report format
- [ ] Show sources with members
- [ ] Show health states
- [ ] Show routing examples

### Phase 8: Testing (Week 4)

- [ ] Unit tests for source election
- [ ] Unit tests for member selection (policy + pinning)
- [ ] Unit tests for fail-fast scenarios
- [ ] Integration tests for discovery
- [ ] Integration tests for multi-source scenarios
- [ ] Update S5.Recs sample and verify embedding works

---

## Testing Strategy

### Unit Tests

```csharp
[Fact]
public void SourceElection_HighestPriority_Selected()
{
    var sources = new[]
    {
        new AiSourceDefinition { Name = "ollama", Priority = 50, ... },
        new AiSourceDefinition { Name = "enterprise", Priority = 100, ... }
    };

    var elected = ElectSource(sources, "Chat");

    Assert.Equal("enterprise", elected.Name);
}

[Fact]
public void MemberPinning_ValidMember_Selected()
{
    var source = new AiSourceDefinition
    {
        Name = "ollama",
        Members = [
            new AiMemberDefinition { Name = "ollama::host", ... },
            new AiMemberDefinition { Name = "ollama::container", ... }
        ]
    };

    var member = SelectMember(source, sourceHint: "ollama::host");

    Assert.Equal("ollama::host", member.Name);
}

[Fact]
public void MemberPinning_InvalidMember_ThrowsException()
{
    var source = new AiSourceDefinition { Name = "ollama", Members = [...] };

    var ex = Assert.Throws<InvalidOperationException>(
        () => SelectMember(source, sourceHint: "ollama::nonexistent"));

    Assert.Contains("Member 'ollama::nonexistent' not found", ex.Message);
    Assert.Contains("Available members:", ex.Message);
}

[Fact]
public void Discovery_ExplicitUrls_SkipsDiscovery()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Koan:Ai:Ollama:Urls:0"] = "http://custom:11434"
        })
        .Build();

    var service = new OllamaDiscoveryService(...);
    await service.StartAsync(CancellationToken.None);

    var source = _sourceRegistry.GetSource("ollama");
    Assert.Single(source.Members);
    Assert.Equal("ollama::explicit-1", source.Members[0].Name);
}
```

---

## Success Criteria

✅ **Zero config works:** `Ai.Chat("Hello")` succeeds with no configuration

✅ **Source election:** Highest-priority source with capability is selected

✅ **Member pinning:** `Source = "ollama::host"` pins to specific member

✅ **Fail-fast:** Invalid source/member throws clear exception immediately

✅ **Discovery semantics:** `Urls` disables discovery, `AdditionalUrls` extends it

✅ **Policy works:** Fallback, RoundRobin, WeightedRoundRobin select members correctly

✅ **Health monitoring:** Circuit breakers per member, source health aggregated

✅ **Caching:** Introspection results cached, fast startup on subsequent runs

✅ **Boot report:** Hierarchical display shows sources → members → capabilities

✅ **Logging:** Single Info/Error logs show full routing decision with outcome

✅ **S5.Recs works:** Embedding requests complete successfully

---

## Consequences

### Positive

✅ **Clear mental model:** Source (collection) → Members (endpoints) is intuitive

✅ **Flexible routing:** Support both high-level (source + policy) and low-level (member pinning)

✅ **Fail-fast semantics:** Invalid references caught immediately with clear errors

✅ **Production-ready:** Health monitoring, circuit breakers, multi-member resilience

✅ **Zero-config principle:** Discovery works out-of-box, config is optional overlay

✅ **Testability:** Easy to mock sources/members for unit tests

✅ **Observable:** Detailed boot report and one-liner request logs

### Negative

⚠️ **Breaking changes:** Requires data model refactor (major version bump)

⚠️ **Complexity:** Two-level hierarchy (source/member) adds cognitive load

⚠️ **Migration effort:** Existing samples need config updates

⚠️ **Cache management:** Persistent cache requires volume mount in containers

### Mitigations

- **Backward compatibility:** Legacy `Koan:Ai:Ollama` config still works
- **Progressive disclosure:** Simple cases (zero config) remain simple
- **Clear errors:** Fail-fast with actionable messages guides users
- **Documentation:** This ADR is comprehensive and canonical

---

## References

- **Supersedes:** ADR-0014, AI-SOURCE-ROUTING-FIX, AI-SOURCE-MEMBER-IMPLEMENTATION-PLAN
- **Related:** ADR-0011 (AI Engine facade)
- **Inspired by:** Koan.Data source/adapter pattern

---

## Appendix: Configuration Examples

### Production Multi-Source Setup

```json
{
  "Koan": {
    "Ai": {
      "Policy": "Fallback",
      "CircuitBreaker": {
        "FailureThreshold": 5,
        "BreakDurationSeconds": 60
      },
      "Sources": {
        "production": {
          "Priority": 100,
          "Policy": "RoundRobin",
          "Ollama": {
            "Urls": [
              "http://ollama-prod-1:11434",
              "http://ollama-prod-2:11434",
              "http://ollama-prod-3:11434"
            ],
            "Capabilities": {
              "Chat": { "Model": "llama3.2:70b" },
              "Embedding": { "Model": "nomic-embed-text" }
            }
          }
        },
        "fallback": {
          "Priority": 50,
          "Policy": "Fallback",
          "Ollama": {
            "Urls": ["http://ollama-backup:11434"],
            "Capabilities": {
              "Chat": { "Model": "llama3.2:8b" },
              "Embedding": { "Model": "all-minilm" }
            }
          }
        }
      }
    }
  }
}
```

**Result:**
- Primary: "production" with 3 members (RoundRobin load balancing)
- Fallback: "fallback" with 1 member (only used if production down)
- Automatic failover if production source becomes unhealthy

---

**END OF SPECIFICATION**
