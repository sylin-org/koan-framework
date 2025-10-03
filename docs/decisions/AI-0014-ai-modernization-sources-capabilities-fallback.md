# AI-0014: AI Modernization - Source Abstraction, Capability Mapping, and Fallback Groups

Status: Approved
**Amendment:** 2025-10-01 - Terminology Correction (See Below)

---

## 🔴 TERMINOLOGY AMENDMENT (2025-10-01)

**This ADR's original terminology has been corrected to match framework standards:**

| Original Term (This Doc) | Correct Term | Definition |
|-------------------------|--------------|------------|
| "Source" | **Member** | Individual endpoint (e.g., `ollama::host`) |
| "Group" | **Source** | Collection of members (e.g., `ollama`) |
| "ollama-auto-host" | **ollama::host** | Member naming pattern |
| "ollama-auto" group | **ollama** source | Source naming pattern |

**Canonical Reference:** See [AI-SOURCE-MEMBER-ARCHITECTURE.md](../AI-SOURCE-MEMBER-ARCHITECTURE.md) for complete model.

**Key Changes:**
- **Source = Collection** with policy and multiple members
- **Member = Endpoint** with URL and priority
- **Default source = provider name** (e.g., "ollama" for Ollama adapter)
- **Naming: `source::member`** pattern (e.g., "ollama::host", "enterprise::ollama-1")

**Reading This Document:**
- When you see "source" below, think "member" (endpoint)
- When you see "group" below, think "source" (collection)
- When you see "ollama-auto-host", think "ollama::host"
- The architectural principles remain valid; only terminology changed

---

## Context

Current Koan.AI implementation (as of 0.2.x) provides solid foundation with `IAiRouter`, `IAiAdapter`, and auto-registration patterns. However, several limitations prevent enterprise adoption and optimal developer experience:

### Current Limitations

1. **No Multi-Instance Support**: Cannot configure multiple instances of same provider (e.g., Ollama-local + Ollama-cloud)
2. **Manual Model Selection**: Developers must know specific model names; no capability-based routing
3. **No Resilience**: Single adapter failure causes request failure; no automatic fallback
4. **Configuration Duplication**: Same provider/endpoint repeated across multiple model configurations
5. **Ambiguous API**: Mix of `Ai.Prompt()` and `Ai.Embed()` with unclear model selection semantics
6. **Limited Discovery**: Auto-discovery creates single source; doesn't expose all available Ollama instances
7. **Manual Model Management**: Developers must manually run `ollama pull` commands before using models

### Decision Drivers

- **Zero-Config Principle**: Must work out-of-box with package reference only
- **Progressive Enhancement**: Simple cases simple, complex cases possible
- **Framework Consistency**: Align with Koan.Data's source abstraction pattern
- **Production Resilience**: Automatic failover, health monitoring, circuit breakers
- **Clear Semantics**: Source determines model; no "source + model override" ambiguity
- **Observable Behavior**: Boot reports show what was discovered and how it's configured

## Decision

Adopt **Source Abstraction with Property-Based Grouping and Capability Mapping** architecture.

### Core Principles

1. **Sources as Configuration Units**: Named configurations combining provider, endpoint, and capability-to-model mappings
2. **Property-Based Grouping**: Sources declare group membership via `Group` property; groups define policies
3. **Capability-First API**: Methods named by capability (`Chat`, `Embed`, `Understand`) instead of generic `Prompt`
4. **Automatic Fallback Groups**: Auto-discovered sources form fallback groups with priority-based routing
5. **Context-Based Overrides**: `Ai.Context(source: "name")` for scoped source selection
6. **Options Pattern**: Single extensible options record per capability instead of multiple overloads
7. **Model Auto-Provisioning**: Configured models automatically downloaded on first use; requests block until ready

### Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│  Developer API (Ai.Chat, Ai.Embed, Ai.Understand)   │
│  + Ai.Context(source, provider, model)              │
├─────────────────────────────────────────────────────┤
│  AI Sources (named configurations)                  │
│  - Provider + BaseUrl + Capabilities + Group        │
├─────────────────────────────────────────────────────┤
│  AI Groups (policy + health monitoring)             │
│  - Fallback, RoundRobin, WeightedRoundRobin        │
├─────────────────────────────────────────────────────┤
│  AiSourceRegistry + AiGroupRegistry                 │
├─────────────────────────────────────────────────────┤
│  IAiAdapterFactory (source-aware creation)          │
├─────────────────────────────────────────────────────┤
│  Concrete Adapters (Ollama, OpenAI, Anthropic)     │
└─────────────────────────────────────────────────────┘
```

## Configuration Schema

### Minimal Configurations

#### Level 0: Zero Config (Auto-Discovery)
```json
{}
```

**Behavior:**
- Discovers Ollama at localhost:11434, host.docker.internal:11434, linked services
- Introspects installed models
- Maps models to capabilities using preferred model lists
- Creates sources: `ollama-auto-host`, `ollama-auto-linked`, `ollama-auto-container`
- Forms fallback group: `ollama-auto` with priority-based ordering
- Sets highest-priority source as default

**Boot Report:**
```
┌─ Koan FRAMEWORK v0.2.18 ─────────────────
│ AI Sources (3 auto-discovered)
│   ├─ ollama-auto-host (Priority: 100) 🌟 DEFAULT
│   │   ├─ Location: host.docker.internal:11434
│   │   ├─ Group: ollama-auto
│   │   ├─ Capabilities: Chat→llama3.2, Embedding→nomic-embed-text, Vision→llava
│   │   └─ Status: ✓ Healthy
│   ├─ ollama-auto-linked (Priority: 75)
│   │   └─ Group: ollama-auto
│   └─ ollama-auto-container (Priority: 50)
│       └─ Group: ollama-auto
│
│ Groups (1 configured)
│   └─ ollama-auto
│       ├─ Policy: Fallback
│       ├─ Members: ollama-auto-host → ollama-auto-linked → ollama-auto-container
│       └─ Health monitoring: ✓ Enabled (probe every 30s)
```

#### Level 1: Simple Override
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
- Auto-discovery still occurs
- Uses specified model for all capabilities in discovered sources
- Backward compatible with existing configurations

#### Level 2: Capability-Specific Models
```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "Capabilities": {
          "Chat": { "Model": "llama3.2", "Temperature": 0.7 },
          "Embedding": { "Model": "nomic-embed-text" },
          "Vision": { "Model": "llava" }
        }
      }
    }
  }
}
```

**Behavior:**
- Auto-discovery creates "Default" source with these capability mappings
- Different models for different capabilities
- Capability-specific options (temperature, max tokens)
- **Models auto-downloaded if missing** (Ollama only)

### Advanced Configurations

#### Level 3: Explicit Sources with Groups
```json
{
  "Koan": {
    "Ai": {
      "Sources": {
        "ollama-primary": {
          "Provider": "ollama",
          "BaseUrl": "http://ollama1:11434",
          "Group": "production-ollama",
          "Priority": 100,
          "Capabilities": {
            "Chat": { "Model": "llama3.2" },
            "Embedding": { "Model": "nomic-embed-text" }
          }
        },
        "ollama-secondary": {
          "Provider": "ollama",
          "BaseUrl": "http://ollama2:11434",
          "Group": "production-ollama",
          "Priority": 75,
          "Capabilities": {
            "Chat": { "Model": "llama3.2" },
            "Embedding": { "Model": "nomic-embed-text" }
          }
        },
        "openai-premium": {
          "Provider": "openai",
          "ApiKey": "${OPENAI_API_KEY}",
          "Group": "cloud-services",
          "Priority": 100,
          "Capabilities": {
            "Chat": { "Model": "gpt-4o" },
            "Vision": { "Model": "gpt-4o" }
          }
        }
      },
      "Groups": {
        "production-ollama": {
          "Policy": "Fallback",
          "HealthCheck": {
            "Enabled": true,
            "IntervalSeconds": 30,
            "TimeoutSeconds": 5
          },
          "CircuitBreaker": {
            "FailureThreshold": 3,
            "BreakDurationSeconds": 30
          }
        },
        "cloud-services": {
          "Policy": "RoundRobin",
          "StickySession": true
        }
      },
      "Routing": {
        "DefaultSource": "ollama-primary",
        "CapabilityDefaults": {
          "Chat": "production-ollama",
          "Embedding": "production-ollama",
          "Vision": "openai-premium"
        }
      }
    }
  }
}
```

**Behavior:**
- Chat/Embedding use `production-ollama` group (fallback policy)
- Vision uses `openai-premium` source
- Automatic failover within `production-ollama` group
- Health monitoring with circuit breakers
- Round-robin for cloud services

## Model Auto-Provisioning

### Philosophy

**"Reference = Intent"** - If a model is configured, the framework ensures it's available. Developers shouldn't need to manually run `ollama pull` commands.

### Behavior by Provider

#### Ollama (Supports Auto-Download)
- Models configured in capabilities are checked during discovery
- Missing models flagged in boot report with download size
- **On first request**: Model automatically downloaded, request blocks until complete
- Download progress logged: `[INFO] Downloading llama3.2 (4.7GB) - 45% complete`
- Subsequent requests use cached model

#### OpenAI, Anthropic, Azure (Managed Services)
- No model provisioning needed (cloud-managed)
- Invalid model names fail immediately with helpful error

### Configuration Control

```json
{
  "Koan": {
    "Ai": {
      "ModelProvisioning": {
        "Enabled": true,                    // Default: true in Development, false in Production
        "TimeoutSeconds": 600,              // Max wait time for download (10 minutes)
        "BlockRequests": true,              // Default: true (wait for download)
        "DownloadOnBoot": false,            // Default: false (download on first use)
        "AllowInProduction": false          // Default: false (safety guard)
      },
      "Ollama": {
        "Capabilities": {
          "Chat": {
            "Model": "llama3.2",
            "AutoDownload": true            // Per-model override (default: inherits from global)
          }
        }
      }
    }
  }
}
```

### Boot Report with Provisioning

```
┌─ Koan FRAMEWORK v0.2.18 ─────────────────
│ AI Sources (1 auto-discovered)
│   └─ ollama-auto-local
│       ├─ Provider: ollama (http://localhost:11434)
│       ├─ Capabilities:
│       │   ├─ Chat → llama3.2 (✓ available, 4.7GB)
│       │   ├─ Embedding → nomic-embed-text (⚠ missing, 274MB - will download on first use)
│       │   └─ Vision → llava (⚠ missing, 4.5GB - will download on first use)
│       └─ Status: ✓ Ready
│
│ Model Provisioning
│   ├─ Enabled: ✓ (Development mode)
│   ├─ Strategy: Download on first use (non-blocking boot)
│   └─ Missing models: 2 (will download automatically)
│
│ Recommendations
│   └─ ℹ Pre-download models to avoid first-request delay:
│       ollama pull nomic-embed-text && ollama pull llava
```

### Request Behavior

#### First Request with Missing Model

**Option A: Block and Wait (Default)**
```csharp
var response = await Ai.Chat("Hello");  // Model missing

// Logs:
// [INFO] Model 'llama3.2' not found, downloading... (4.7GB)
// [INFO] Download progress: 10%... 25%... 50%... 75%... 100%
// [INFO] Model 'llama3.2' ready
// [INFO] Request completed

// Response time: ~5-10 minutes for large model
```

**Option B: Fail Fast with Retry Guidance**
```json
{
  "Koan": {
    "Ai": {
      "ModelProvisioning": {
        "BlockRequests": false  // Don't block, fail immediately
      }
    }
  }
}
```

```csharp
try {
    var response = await Ai.Chat("Hello");
}
catch (ModelProvisioningException ex) {
    // Exception message:
    // "Model 'llama3.2' is not available and auto-provisioning is disabled.
    //  Run: ollama pull llama3.2
    //  Or enable: Koan:Ai:ModelProvisioning:Enabled = true"
}
```

#### Subsequent Requests
```csharp
var response = await Ai.Chat("Hello");  // Instant - model cached
```

### Safety Guards

#### Production Protection
```csharp
// In production, auto-download disabled by default
if (KoanEnv.IsProduction && !KoanEnv.AllowMagicInProduction) {
    if (_options.ModelProvisioning.Enabled) {
        _logger.LogWarning(
            "Model auto-provisioning enabled in production. " +
            "Set AllowMagicInProduction=true to acknowledge.");

        // Allow, but warn prominently in boot report
    }
}
```

#### Timeout Handling
```csharp
try {
    await DownloadModelAsync(modelName, timeout: TimeSpan.FromSeconds(600));
}
catch (TimeoutException) {
    throw new ModelProvisioningException(
        $"Model '{modelName}' download timed out after 600 seconds. " +
        $"Increase Koan:Ai:ModelProvisioning:TimeoutSeconds or download manually.");
}
```

### Advanced: Download on Boot

```json
{
  "Koan": {
    "Ai": {
      "ModelProvisioning": {
        "DownloadOnBoot": true  // Download during startup instead of first use
      }
    }
  }
}
```

**Behavior:**
- During `OllamaDiscoveryService.StartAsync()`, trigger downloads for all missing models
- Boot completes when all models ready
- Startup time increases, but first request is fast
- Ideal for production deployments (pre-warm models)

### Implementation: IModelManager Interface

```csharp
public interface IModelManager {
    /// <summary>
    /// Check if model is available locally.
    /// </summary>
    Task<bool> IsAvailableAsync(string model, CancellationToken ct = default);

    /// <summary>
    /// Download model if not available. Blocks until complete.
    /// </summary>
    Task<ModelDownloadResult> EnsureAvailableAsync(
        string model,
        TimeSpan? timeout = null,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get model metadata (size, family, capabilities).
    /// </summary>
    Task<ModelMetadata?> GetMetadataAsync(string model, CancellationToken ct = default);
}

public record ModelDownloadResult(
    bool Success,
    string Model,
    TimeSpan Duration,
    long BytesDownloaded,
    string? ErrorMessage);

public record ModelDownloadProgress(
    string Model,
    long BytesDownloaded,
    long? TotalBytes,
    double? Percentage,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining);
```

### Ollama Adapter Integration

```csharp
public class OllamaAdapter : IAiAdapter {
    private readonly IModelManager _modelManager;

    public async Task<AiChatResponse> ChatAsync(
        AiChatRequest request,
        CancellationToken ct)
    {
        // Ensure model available before making request
        if (_provisioningOptions.Enabled) {
            var available = await _modelManager.IsAvailableAsync(request.Model, ct);

            if (!available) {
                _logger.LogInformation(
                    "Model '{Model}' not found, downloading...",
                    request.Model);

                var downloadResult = await _modelManager.EnsureAvailableAsync(
                    request.Model,
                    timeout: TimeSpan.FromSeconds(_provisioningOptions.TimeoutSeconds),
                    progress: new Progress<ModelDownloadProgress>(p => {
                        _logger.LogInformation(
                            "Downloading {Model}: {Percentage:F1}% ({Downloaded}/{Total})",
                            p.Model,
                            p.Percentage ?? 0,
                            FormatBytes(p.BytesDownloaded),
                            FormatBytes(p.TotalBytes ?? 0));
                    }),
                    ct);

                if (!downloadResult.Success) {
                    throw new ModelProvisioningException(
                        $"Failed to download model '{request.Model}': {downloadResult.ErrorMessage}");
                }

                _logger.LogInformation(
                    "Model '{Model}' ready after {Duration}",
                    request.Model,
                    downloadResult.Duration);
            }
        }

        // Proceed with chat request
        return await ExecuteChatAsync(request, ct);
    }
}
```

## Developer API

### Capability-First Static Methods
```csharp
public static class Ai {
    // Simple usage
    public static Task<string> Chat(string message, CancellationToken ct = default);
    public static Task<float[]> Embed(string text, CancellationToken ct = default);
    public static Task<string> Understand(byte[] imageBytes, string prompt, CancellationToken ct = default);

    // Advanced usage with options
    public static Task<string> Chat(AiChatOptions options, CancellationToken ct = default);
    public static Task<float[]> Embed(AiEmbedOptions options, CancellationToken ct = default);
    public static Task<string> Understand(AiVisionOptions options, CancellationToken ct = default);

    // Streaming
    public static IAsyncEnumerable<string> Stream(string message, CancellationToken ct = default);
    public static IAsyncEnumerable<string> Stream(AiChatOptions options, CancellationToken ct = default);

    // Context management
    public static AiContextScope Context(
        string? source = null,
        string? provider = null,
        string? model = null);
}
```

### Options Records
```csharp
public record AiOptionsBase {
    public string? Source { get; init; }      // Source or group name
    public string? Provider { get; init; }    // Fallback: provider name
    public string? Model { get; init; }       // Escape hatch: model override
}

public record AiChatOptions : AiOptionsBase {
    public required string Message { get; init; }
    public string? SystemPrompt { get; init; }
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public List<AiMessage>? ConversationHistory { get; init; }
}

public record AiEmbedOptions : AiOptionsBase {
    public string? Text { get; init; }
    public string[]? Texts { get; init; }  // Batch operation
}

public record AiVisionOptions : AiOptionsBase {
    public required byte[] ImageBytes { get; init; }
    public required string Prompt { get; init; }
    public float? Temperature { get; init; }
}
```

### Context-Based Overrides
```csharp
// Default behavior (uses routing configuration)
var response = await Ai.Chat("Hello");

// Override source for specific operations
using (Ai.Context(source: "ollama-primary")) {
    var response = await Ai.Chat("Process locally");
}

// Nested contexts (inner overrides outer)
using (Ai.Context(source: "production-ollama")) {  // Group
    var chat = await Ai.Chat("Question 1");

    using (Ai.Context(source: "cloud-services")) {  // Different group
        var vision = await Ai.Understand(imageBytes, "What's this?");
    }
}

// Model override (escape hatch - discouraged)
using (Ai.Context(source: "ollama-primary", model: "llama3.2:70b")) {
    var response = await Ai.Chat("Test with specific model");
}
```

## Scenarios and Expected Outcomes

### Scenario 1: Developer First Run (Zero Config)

**Setup:**
- Fresh project
- Add `<PackageReference Include="Koan.AI.Connector.Ollama" />`
- Ollama running on localhost with llama3.2 and nomic-embed-text installed

**Code:**
```csharp
var response = await Ai.Chat("What is quantum computing?");
var embeddings = await Ai.Embed("Document text");
```

**Expected Outcome:**
✅ Works immediately
✅ Uses llama3.2 for chat (auto-discovered)
✅ Uses nomic-embed-text for embedding (auto-discovered)
✅ Boot report shows: `ollama-auto-local` source with 2 capabilities
✅ Logs: `[INFO] Auto-configured AI source 'ollama-auto-local' with 2 capabilities`

### Scenario 2: Containerized App with Host Ollama

**Setup:**
- App running in Docker container
- Ollama on host machine (http://host.docker.internal:11434)
- Ollama has llama3.2, llava, nomic-embed-text

**Code:**
```csharp
var chat = await Ai.Chat("Analyze this data");
var vision = await Ai.Understand(imageBytes, "Describe this chart");
```

**Expected Outcome:**
✅ Discovers ollama-auto-host (priority 100)
✅ Boot report explains: "Host models persist across container restarts"
✅ Chat uses llama3.2, Vision uses llava
✅ All operations route to host by default

### Scenario 3: Multi-Source Fallback

**Setup:**
- Ollama on host (http://host.docker.internal:11434)
- Ollama on linked service (http://ollama:11434)
- Both discovered automatically

**Code:**
```csharp
var response = await Ai.Chat("Hello");
// Host Ollama crashes mid-operation
```

**Expected Outcome:**
✅ Initial requests use ollama-auto-host (priority 100)
✅ After 3 failures, circuit opens for ollama-auto-host
✅ Automatic failover to ollama-auto-linked (priority 75)
✅ Logs: `[WARN] Source 'ollama-auto-host' failed, trying 'ollama-auto-linked'`
✅ Background service probes ollama-auto-host every 30s
✅ When host recovers, automatically switches back
✅ Logs: `[INFO] Source 'ollama-auto-host' recovered and is now healthy`

### Scenario 4: Hybrid Local/Cloud Strategy

**Configuration:**
```json
{
  "Koan": {
    "Ai": {
      "Sources": {
        "local-ollama": {
          "Provider": "ollama",
          "Capabilities": {
            "Chat": { "Model": "llama3.2" },
            "Embedding": { "Model": "nomic-embed-text" }
          }
        },
        "cloud-vision": {
          "Provider": "openai",
          "ApiKey": "${OPENAI_API_KEY}",
          "Capabilities": {
            "Vision": { "Model": "gpt-4o" }
          }
        }
      },
      "Routing": {
        "CapabilityDefaults": {
          "Chat": "local-ollama",
          "Embedding": "local-ollama",
          "Vision": "cloud-vision"
        }
      }
    }
  }
}
```

**Code:**
```csharp
var chat = await Ai.Chat("Summarize this");           // Uses local
var embeddings = await Ai.Embed("Document text");     // Uses local
var vision = await Ai.Understand(image, "Describe");  // Uses cloud
```

**Expected Outcome:**
✅ Chat and Embedding use local Ollama (cost-free, private)
✅ Vision uses OpenAI (more capable)
✅ Automatic routing based on capability
✅ No code changes needed to switch providers

### Scenario 5: Multi-Tenant Isolation

**Configuration:**
```json
{
  "Koan": {
    "Ai": {
      "Sources": {
        "tenant-basic": {
          "Provider": "ollama",
          "Capabilities": {
            "Chat": { "Model": "llama3.2:3b" }
          }
        },
        "tenant-premium": {
          "Provider": "openai",
          "ApiKey": "${OPENAI_PREMIUM_KEY}",
          "Capabilities": {
            "Chat": { "Model": "gpt-4o" }
          }
        }
      }
    }
  }
}
```

**Code:**
```csharp
public class TenantService {
    public async Task<string> ProcessQuery(string tenantId, string query) {
        var sourceName = tenantId == "premium" ? "tenant-premium" : "tenant-basic";

        using (Ai.Context(source: sourceName)) {
            return await Ai.Chat(query);
        }
    }
}
```

**Expected Outcome:**
✅ Premium tenant uses GPT-4o
✅ Basic tenant uses local llama3.2:3b
✅ Complete isolation per tenant
✅ Single codebase handles both tiers

### Scenario 6: Testing Different Models

**Code:**
```csharp
var prompt = "Explain quantum computing";
var models = new[] { "llama3.2:3b", "llama3.2:8b", "llama3.2:70b" };

foreach (var model in models) {
    using (Ai.Context(source: "ollama-primary", model: model)) {
        var sw = Stopwatch.StartNew();
        var response = await Ai.Chat(prompt);
        sw.Stop();

        Console.WriteLine($"{model}: {sw.ElapsedMilliseconds}ms - {response.Length} chars");
    }
}
```

**Expected Outcome:**
✅ Tests each model on same source
✅ Model override works within context
✅ Performance comparison easy to implement
✅ No configuration changes needed

### Scenario 7: Model Auto-Provisioning (Zero Manual Setup)

**Setup:**
- Fresh Ollama installation (no models installed)
- Configuration specifies models not yet downloaded

**Configuration:**
```json
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "Capabilities": {
          "Chat": { "Model": "llama3.2" },
          "Embedding": { "Model": "nomic-embed-text" }
        }
      },
      "ModelProvisioning": {
        "Enabled": true,
        "BlockRequests": true
      }
    }
  }
}
```

**Code:**
```csharp
// First request with missing model
Console.WriteLine("Sending first chat request...");
var response = await Ai.Chat("What is quantum computing?");
Console.WriteLine($"Response: {response}");
```

**Expected Outcome:**

**Boot Report:**
```
┌─ Koan FRAMEWORK v0.2.18 ─────────────────
│ AI Sources (1 auto-discovered)
│   └─ ollama-auto-local
│       ├─ Capabilities:
│       │   ├─ Chat → llama3.2 (⚠ missing, 4.7GB - will download on first use)
│       │   └─ Embedding → nomic-embed-text (⚠ missing, 274MB - will download on first use)
│       └─ Status: ✓ Ready
│
│ Model Provisioning: ✓ Enabled
│   └─ Strategy: Download on first use (requests block until complete)
```

**First Request Logs:**
```
[INFO] Model 'llama3.2' not found, downloading... (4.7GB)
[INFO] Downloading llama3.2: 5.0% (240MB/4.7GB) - ETA 8m 30s
[INFO] Downloading llama3.2: 15.0% (720MB/4.7GB) - ETA 7m 10s
[INFO] Downloading llama3.2: 50.0% (2.4GB/4.7GB) - ETA 3m 20s
[INFO] Downloading llama3.2: 100.0% (4.7GB/4.7GB) - Complete
[INFO] Model 'llama3.2' ready after 00:08:45
[INFO] Request completed successfully
```

**Outcome:**
✅ No manual `ollama pull` needed
✅ Request blocks but completes successfully
✅ Progress logged every 10%
✅ Subsequent requests instant (model cached)
✅ Boot fast (download deferred to first use)
✅ Developer sees clear progress indication

**Alternative: Fail Fast Mode**
```json
{
  "Koan": {
    "Ai": {
      "ModelProvisioning": {
        "BlockRequests": false  // Fail instead of blocking
      }
    }
  }
}
```

**First Request:**
```csharp
try {
    var response = await Ai.Chat("Hello");
}
catch (ModelProvisioningException ex) {
    Console.WriteLine(ex.Message);
    // Output:
    // "Model 'llama3.2' is not available and auto-provisioning is disabled.
    //  Run: ollama pull llama3.2
    //  Or enable: Koan:Ai:ModelProvisioning:BlockRequests = true"
}
```

✅ Immediate failure with actionable guidance
✅ Developer chooses: manual download or enable auto-provisioning

## Implementation Phases

### Phase 1: Source Abstraction Foundation (Week 1-2)
**Goal:** Basic source registry and configuration parsing

**Tasks:**
1. Create `AiSourceDefinition` record with Group and Priority properties
2. Create `AiSourceRegistry` with discovery from `Koan:Ai:Sources` configuration
3. Implement backward-compatible parsing of `Koan:Ai:Ollama` simple config
4. Create `AiGroupDefinition` record and `AiGroupRegistry`
5. Update `OllamaDiscoveryService` to create sources with auto-discovery naming

**Tests:**
- [ ] Zero config creates implicit "Default" source
- [ ] Simple `Koan:Ai:Ollama:DefaultModel` creates source with single model for all capabilities
- [ ] Capability-specific `Koan:Ai:Ollama:Capabilities` config parsed correctly
- [ ] Explicit `Koan:Ai:Sources` configuration creates named sources
- [ ] Source with Group property discovered and registered
- [ ] Boot report shows all discovered sources

**Acceptance Criteria:**
```csharp
// This must work with zero config:
var registry = services.GetRequiredService<AiSourceRegistry>();
var defaultSource = registry.GetSource("Default");
Assert.NotNull(defaultSource);
Assert.Equal("ollama", defaultSource.Provider);
```

### Phase 2: Capability-First API (Week 2-3)
**Goal:** New developer-facing API with options pattern

**Tasks:**
1. Create options records: `AiChatOptions`, `AiEmbedOptions`, `AiVisionOptions`
2. Implement `Ai.Chat()`, `Ai.Embed()`, `Ai.Understand()` methods
3. Implement `Ai.Stream()` for streaming responses
4. Create `AiContextScope` with stack-based context management
5. Implement `Ai.Context(source, provider, model)` factory method
6. Update adapter resolution to check context before routing config

**Tests:**
- [ ] Simple `Ai.Chat("message")` works with default source
- [ ] Options-based `Ai.Chat(new AiChatOptions { Message = "..." })` works
- [ ] Context override: `using (Ai.Context(source: "x")) { ... }` routes correctly
- [ ] Nested contexts: inner overrides outer
- [ ] Model override via context works
- [ ] Context stack cleaned up after dispose

**Acceptance Criteria:**
```csharp
// Simple usage
var response = await Ai.Chat("Hello");
Assert.NotNull(response);

// Context-based routing
using (Ai.Context(source: "test-source")) {
    var response = await Ai.Chat("Hello");
    // Verify routed to test-source
}
```

### Phase 3: Multi-Source Discovery + Model Provisioning (Week 3-4)
**Goal:** Discover multiple Ollama instances, create fallback group, and enable model auto-provisioning

**Tasks:**
1. Enhance `OllamaDiscoveryService` to check host, localhost, linked services
2. Create `ollama-auto-host`, `ollama-auto-linked`, `ollama-auto-container` sources
3. Implement model introspection via `/api/tags`
4. Create capability mapping with preferred model lists
5. Form `ollama-auto` fallback group with discovered sources
6. Register alias "Default" → highest priority auto-discovered source
7. **Implement `IModelManager` interface for Ollama**
8. **Add model availability checking and download logic**
9. **Integrate model provisioning into adapter Chat/Embed/Vision methods**
10. Enhanced boot report showing all discovered sources, groups, and missing models

**Tests:**
- [ ] Containerized: discovers host Ollama first
- [ ] Containerized: discovers linked Ollama service
- [ ] Containerized: discovers container localhost Ollama
- [ ] Non-containerized: discovers localhost Ollama as `ollama-auto-local`
- [ ] Multiple sources form `ollama-auto` group
- [ ] Sources have correct priority (host=100, linked=75, container=50)
- [ ] Default alias points to highest priority source
- [ ] Boot report shows all sources with locations
- [ ] **Boot report flags missing models with sizes**
- [ ] **Model availability check via `IModelManager.IsAvailableAsync()`**
- [ ] **Model download with progress reporting**
- [ ] **First request blocks until model downloaded**
- [ ] **Subsequent requests use cached model (instant)**
- [ ] **BlockRequests=false fails immediately with helpful error**
- [ ] **Production guard: auto-download disabled by default**

**Acceptance Criteria:**
```csharp
// When containerized with host and linked Ollama:
var registry = services.GetRequiredService<AiSourceRegistry>();
var hostSource = registry.GetSource("ollama-auto-host");
var linkedSource = registry.GetSource("ollama-auto-linked");

Assert.NotNull(hostSource);
Assert.NotNull(linkedSource);
Assert.Equal(100, hostSource.Priority);
Assert.Equal(75, linkedSource.Priority);
Assert.Equal("ollama-auto", hostSource.Group);
Assert.Equal("ollama-auto", linkedSource.Group);

// Model provisioning
var modelManager = services.GetRequiredService<IModelManager>();
var available = await modelManager.IsAvailableAsync("llama3.2");
Assert.False(available);  // Model not installed

// First request triggers download
var response = await Ai.Chat("Hello");  // Blocks, downloads model
Assert.NotNull(response);

// Verify model now available
available = await modelManager.IsAvailableAsync("llama3.2");
Assert.True(available);  // Model cached

// Second request instant
var sw = Stopwatch.StartNew();
response = await Ai.Chat("Hello again");
sw.Stop();
Assert.True(sw.ElapsedMilliseconds < 1000);  // No download delay
```

### Phase 4: Fallback Groups and Circuit Breaker (Week 4-5)
**Goal:** Automatic failover with health monitoring

**Tasks:**
1. Implement `IGroupPolicy` interface with `Fallback`, `RoundRobin` policies
2. Create `CircuitBreakerState` per source
3. Implement `ResilientAiAdapter` wrapper that tries sources in priority order
4. Create `AiSourceHealthMonitor` background service
5. Implement health probing with configurable intervals
6. Add circuit breaker state transitions (Healthy → Unhealthy → Recovering)
7. Log failover events and recovery

**Tests:**
- [ ] Fallback policy tries sources in priority order
- [ ] Circuit opens after configured failure threshold
- [ ] Requests route to next source when circuit open
- [ ] Background service probes unhealthy sources
- [ ] Circuit closes after successful health probes
- [ ] Requests return to primary after recovery
- [ ] Round-robin policy distributes load evenly
- [ ] Health check respects timeout configuration

**Acceptance Criteria:**
```csharp
// Simulate primary source failure
MockAdapter.Setup(a => a.ChatAsync(...)).Throws<HttpRequestException>();

var response = await Ai.Chat("Hello");
// Should succeed using fallback source

// Verify circuit breaker state
var health = healthRegistry.GetSourceHealth("ollama-auto-host");
Assert.Equal(SourceHealthState.Unhealthy, health.State);

// Simulate recovery
MockAdapter.Setup(a => a.ChatAsync(...)).Returns(successResponse);

await Task.Delay(TimeSpan.FromSeconds(35)); // Wait for health probe

health = healthRegistry.GetSourceHealth("ollama-auto-host");
Assert.Equal(SourceHealthState.Healthy, health.State);
```

### Phase 5: Enhanced Boot Reporting (Week 5)
**Goal:** Comprehensive visibility into AI configuration

**Tasks:**
1. Extend boot report to show sources with capabilities
2. Show group membership and policies
3. Display health status and fallback chains
4. Add usage examples to boot report
5. Show recommendations for missing models
6. Implement structured logging for failover events

**Tests:**
- [ ] Boot report shows all sources
- [ ] Boot report shows groups with members
- [ ] Boot report indicates default source
- [ ] Boot report shows capability→model mappings
- [ ] Boot report warns about missing capabilities
- [ ] Failover events logged with source names

**Acceptance Criteria:**
```
Boot report must include:
✓ Environment (containerized or not)
✓ All discovered sources with locations
✓ Capability→model mappings per source
✓ Group memberships and policies
✓ Default source indication
✓ Health status
✓ Usage examples
```

### Phase 6: OpenAI and Anthropic Connectors (Week 6-7)
**Goal:** Validate multi-provider architecture

**Tasks:**
1. Implement `OpenAIAdapterFactory` following source pattern
2. Implement `OpenAIAdapter` with Chat, Embedding, Vision capabilities
3. Implement `AnthropicAdapterFactory`
4. Implement `AnthropicAdapter` with Chat capability
5. Add auto-discovery for API keys in environment variables
6. Create sources like `openai-auto-default`, `anthropic-auto-default`
7. Update samples to demonstrate multi-provider scenarios

**Tests:**
- [ ] OpenAI adapter registers and resolves via factory
- [ ] OpenAI auto-discovery creates source from `OPENAI_API_KEY`
- [ ] Anthropic adapter works with Claude models
- [ ] Mixed groups: Ollama + OpenAI fallback works
- [ ] Capability routing: local for chat, cloud for vision

**Acceptance Criteria:**
```csharp
// With OPENAI_API_KEY environment variable:
var registry = services.GetRequiredService<AiSourceRegistry>();
var openaiSource = registry.GetSource("openai-auto-default");
Assert.NotNull(openaiSource);

// Mixed provider usage
using (Ai.Context(source: "openai-auto-default")) {
    var response = await Ai.Chat("Complex reasoning");
}
```

### Phase 7: Sample Updates and Documentation (Week 7-8)
**Goal:** Update samples to showcase new features

**Tasks:**
1. Update S5.Recs to use new API
2. Update S12.MedTrials to demonstrate fallback groups
3. Update S13.DocMind to show hybrid local/cloud routing
4. Create new sample: Multi-tenant AI routing
5. Create new sample: Model performance comparison
6. Write migration guide for existing code
7. Update API documentation

**Tests:**
- [ ] All samples build and run
- [ ] Samples demonstrate different configuration levels
- [ ] Migration guide examples work as documented

**Acceptance Criteria:**
```
Updated samples:
✓ S5.Recs: Uses Ai.Embed() for vector search
✓ S12.MedTrials: Demonstrates fallback groups
✓ S13.DocMind: Shows hybrid routing
✓ New sample: Multi-tenant routing working
✓ New sample: Model benchmarking working
```

## Testing Strategy

### Unit Tests
- Source registry configuration parsing
- Group policy behavior (Fallback, RoundRobin)
- Circuit breaker state transitions
- Context stack management
- Adapter resolution priority chain

### Integration Tests
- Auto-discovery with mock Ollama endpoints
- Multi-source fallback behavior
- Health monitoring and recovery
- End-to-end chat/embed/vision operations

### Manual Testing Scenarios
1. Zero-config first run experience
2. Containerized app with host Ollama
3. Host Ollama crash and recovery
4. Performance comparison across models
5. Multi-tenant isolation
6. Hybrid local/cloud routing

## Consequences

### Positive

✅ **Zero-Config Works**: Package reference → immediate functionality
✅ **Multi-Instance Support**: Multiple Ollamas, multiple providers coexist
✅ **Production Resilience**: Automatic failover, health monitoring, circuit breakers
✅ **Clear Semantics**: Source determines model; no ambiguity
✅ **Observable**: Boot reports and logs show exactly what's happening
✅ **Testable**: Easy to test with different sources
✅ **Scalable**: Group policies enable load balancing
✅ **Framework Consistent**: Matches Koan.Data source pattern
✅ **Model Auto-Provisioning**: Configure model name, framework ensures availability
✅ **No Manual Setup**: Never run `ollama pull` commands manually again

### Negative

⚠️ **Breaking Changes**: New API requires migration (major version bump)
⚠️ **Complexity**: More moving parts (sources, groups, policies, circuit breakers, model provisioning)
⚠️ **Model Consistency**: Fallback sources may have different models for same capability
⚠️ **Configuration Learning**: Advanced scenarios require understanding sources and groups
⚠️ **First Request Delay**: Auto-download can block first request for minutes (configurable)
⚠️ **Network Dependency**: Model downloads require internet connectivity

### Migration Path

**Existing Code:**
```csharp
var response = await Ai.PromptAsync("Hello");
```

**New Code (backward compatible):**
```csharp
var response = await Ai.Chat("Hello");  // Simpler API
```

**Configuration Migration:**
```json
// Old (still works)
{
  "Koan": {
    "Ai": {
      "Ollama": {
        "DefaultModel": "llama3.2"
      }
    }
  }
}

// New (recommended for multi-source)
{
  "Koan": {
    "Ai": {
      "Sources": {
        "local": {
          "Provider": "ollama",
          "Capabilities": {
            "Chat": { "Model": "llama3.2" }
          }
        }
      }
    }
  }
}
```

## Follow-Ups

### Immediate (Part of This ADR)
- [x] Source abstraction with group properties
- [x] Capability-first API design
- [x] Multi-source auto-discovery
- [x] Fallback groups with circuit breakers
- [x] Enhanced boot reporting
- [x] Model auto-provisioning with progress tracking

### Future Enhancements (Separate ADRs)
- [ ] Cost tracking per source/capability
- [ ] Latency-based routing
- [ ] Request caching layer
- [ ] Prompt templates system
- [ ] Function calling support
- [ ] Audio capabilities (transcription, TTS)
- [ ] Structured output (JSON schema enforcement)
- [ ] RAG integration patterns

## References

- AI-0008: Adapters and registry (current architecture)
- AI-0009: Multi-service routing and policies (routing foundation)
- DATA-0077: Entity context source/adapter routing (Data pattern inspiration)
- ARCH-0044: Standardized module config and discovery
- ARCH-0049: Unified service metadata and discovery
