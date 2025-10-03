# Provider Readiness System - Framework Enhancement Proposal

**Status:** Approved
**Priority:** High
**Scope:** Framework Enhancement
**Primary Affected:** Koan.Core.Adapters, All External Resource Adapters

## Executive Summary

The Koan Framework currently lacks a systematic way to handle adapter readiness for external resources (databases, AI services, storage, etc.), leading to console errors, race conditions, and poor developer experience during application startup. This proposal introduces a **Provider Readiness System** following existing framework capability patterns to ensure clean initialization and operation gating.

## Problem Statement

### Current Issues
- **Console Error Pollution**: Applications show repeated connection failures during startup (e.g., Couchbase `SocketNotAvailableException`, Ollama model loading failures)
- **Race Conditions**: Entity operations (`Data<T>.All()`) execute before adapters are ready
- **Inconsistent Error Handling**: Different adapters handle "not ready" states differently
- **Poor Developer Experience**: Developers must implement custom retry logic or ignore startup errors

### Affected Adapters (Priority Order)
- **Koan.Data.Connector.Couchbase**: Cluster initialization, bucket creation, collection provisioning
- **Koan.AI.Connector.Ollama**: Model downloading, service availability
- **Koan.Data.Connector.Mongo**: Connection establishment, database/collection creation
- **Koan.Data.Vector.Connector.Weaviate**: Schema provisioning, connectivity verification
- **Koan.Storage.Connector.Local**: Directory creation, permission verification
- **Future Adapters**: Any adapter requiring async initialization of external resources

## Root Cause Analysis

The framework has excellent **service discovery** and **registration** patterns but lacks the bridge between **health monitoring** and **operational readiness**:

1. **Health vs Readiness Confusion**: `IHealthContributor` checks liveness, not operational capability
2. **Synchronous Initialization**: `IKoanInitializer.Initialize()` only supports sync DI registration
3. **No Operation Gating**: Entity operations execute immediately without readiness checks
4. **Adapter-Specific Solutions**: Each adapter implements custom retry/initialization logic

## Solution Overview

Introduce a **capability-based readiness system** in `Koan.Core.Adapters` that:

1. **Follows Existing Patterns**: Uses capability interfaces like `IQueryCapabilities`
2. **Universal Application**: Works for all adapter types (Data, AI, Storage, etc.)
3. **Non-Breaking**: Opt-in enhancement, existing code continues working
4. **Configurable Policies**: Different strategies for different deployment scenarios

### Alignment with Koan Framework Principles

* **Reference = Intent** – Readiness capabilities are automatically surfaced through adapter registrars so merely referencing an adapter package is sufficient to light up the readiness system without bespoke wiring.
* **Provider Transparency** – Operation gating is adapter-agnostic; once providers report Ready, entities continue to behave uniformly regardless of the backing technology.
* **Entity-First Development** – Extension methods wrap existing entity operations so callers continue to invoke `Data<T>` APIs without additional ceremony or capability probing.
* **Deterministic Configuration** – Central policies, deterministic defaults, and explicit overrides ensure startup behavior remains predictable across environments.

### Desirability & Feasibility Snapshot

* **Desirability** – Solves recurring startup instability, improves developer confidence, and introduces consistent observability around adapter readiness state, meeting high-priority DX goals.
* **Feasibility** – Builds on existing capability and hosted-service patterns, enumerates adapter-specific rollout plans, and includes shared helpers (`ReadinessStateManager`, initialization services) to keep implementation straightforward for contributors.

## Technical Design

### Core Capabilities (Koan.Core.Adapters)

```csharp
namespace Koan.Core.Adapters;

/// <summary>
/// Represents the readiness state of an external resource adapter
/// </summary>
public enum AdapterReadinessState
{
    Initializing,   // Starting up, performing async initialization
    Ready,          // Fully operational and ready for requests
    Degraded,       // Partially functional, surfaced alongside explicit limitations
    Failed          // Not operational, requires intervention or retry
}

/// <summary>
/// Defines how operations should behave when adapter is not ready
/// </summary>
public enum ReadinessPolicy
{
    Immediate,      // Fail immediately if not ready (throw exception)
    Hold,          // Wait for readiness up to configured timeout
    Degrade        // Continue with degraded functionality if possible
}

/// <summary>
/// Capability interface for adapters that manage external resource readiness
/// </summary>
public interface IAdapterReadiness
{
    /// <summary>Current readiness state</summary>
    AdapterReadinessState ReadinessState { get; }

    /// <summary>Cheap readiness indicator for hot-path checks (Ready or Degraded)</summary>
    bool IsReady { get; }

    /// <summary>Maximum time to wait for readiness</summary>
    TimeSpan ReadinessTimeout { get; }

    /// <summary>Check if adapter is currently ready</summary>
    Task<bool> IsReadyAsync(CancellationToken ct = default);

    /// <summary>Wait for adapter to become ready</summary>
    Task WaitForReadinessAsync(TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>Notifies when readiness state changes</summary>
    event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged;

    /// <summary>Provides thread-safe access to the shared readiness state manager</summary>
    ReadinessStateManager StateManager { get; }
}

/// <summary>
/// Configuration capability for readiness behavior
/// </summary>
public interface IAdapterReadinessConfiguration
{
    /// <summary>Policy for handling not-ready states</summary>
    ReadinessPolicy Policy { get; }

    /// <summary>Timeout for readiness operations</summary>
    TimeSpan Timeout { get; }

    /// <summary>Whether readiness gating is enabled</summary>
    bool EnableReadinessGating { get; }
}

/// <summary>
/// Capability for adapters requiring async initialization
/// </summary>
public interface IAsyncAdapterInitializer
{
    /// <summary>Perform async initialization of external resources</summary>
    Task InitializeAsync(CancellationToken ct = default);
}

/// <summary>
/// Event args for readiness state changes
/// </summary>
public class ReadinessStateChangedEventArgs : EventArgs
{
    public AdapterReadinessState PreviousState { get; }
    public AdapterReadinessState CurrentState { get; }
    public DateTime Timestamp { get; }

    public ReadinessStateChangedEventArgs(AdapterReadinessState previous, AdapterReadinessState current)
    {
        PreviousState = previous;
        CurrentState = current;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Shared helper that enforces consistent readiness transitions, notifications, and wait semantics
/// </summary>
public sealed class ReadinessStateManager
{
    private readonly object _gate = new();
    private AdapterReadinessState _state = AdapterReadinessState.Initializing;
    private TaskCompletionSource _readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AdapterReadinessState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event EventHandler<ReadinessStateChangedEventArgs>? StateChanged;

    public bool IsReady
    {
        get
        {
            lock (_gate)
            {
                return _state is AdapterReadinessState.Ready or AdapterReadinessState.Degraded;
            }
        }
    }

    public void TransitionTo(AdapterReadinessState newState)
    {
        AdapterReadinessState previous;

        lock (_gate)
        {
            if (_state == newState)
                return;

            previous = _state;
            _state = newState;

            if (newState is AdapterReadinessState.Ready or AdapterReadinessState.Degraded)
            {
                _readySignal.TrySetResult();
            }
            else if (newState == AdapterReadinessState.Initializing && _readySignal.Task.IsCompleted)
            {
                _readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        StateChanged?.Invoke(this, new ReadinessStateChangedEventArgs(previous, newState));
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct)
        => _readySignal.Task.WaitAsync(timeout, ct);
}

/// <summary>
/// Exception thrown when adapter operations are attempted before readiness
/// </summary>
public class AdapterNotReadyException : InvalidOperationException
{
    public AdapterReadinessState CurrentState { get; }
    public string AdapterType { get; }

    public AdapterNotReadyException(string adapterType, AdapterReadinessState state, string message)
        : base(message)
    {
        AdapterType = adapterType;
        CurrentState = state;
    }
}
```

### Readiness State Semantics

* **Initializing → Ready** – Triggered after all blocking dependencies complete successfully.
* **Initializing → Degraded** – Allows adapters to serve critical paths while deferred capabilities warm up.
* **Ready → Degraded** – Initiated when a partial outage is detected; operations must surface limitation metadata alongside results.
* **Any → Failed** – Terminal failure that should raise alerts; adapters may attempt retries but must emit a state change.
* **Failed → Initializing** – Used when recoveries begin; adapters should reset the readiness signal through `ReadinessStateManager`.

All transitions must be coordinated through the helper to ensure thread-safe notifications and reliable wait semantics for dependents.

### Fast-Path Readiness Checks

Some adapters execute lightweight runtime operations where awaiting the asynchronous `IsReadyAsync` call would introduce avoidable allocations. The proposal therefore includes a synchronous `IsReady` flag sourced from the shared `ReadinessStateManager` to offer a cheap check in latency-sensitive scenarios.

**Pros**

* Enables micro-optimizations inside hot paths (e.g., caching layers) without requiring adapters to materialize throwaway `Task` instances.
* Provides a consistent, thread-safe indicator because the value is derived from the centralized state manager instead of bespoke adapter fields.

**Cons / Mitigations**

* Risk of adapters bypassing policy enforcement—documentation must emphasize that `IsReady` is informational and should not replace gating when policies demand waits or exceptions.
* Potential divergence if adapters hand-roll their own flags—by exposing the property directly on `ReadinessStateManager` and requiring implementations to delegate, we avoid duplicated state.
* Degraded mode nuance—guidance clarifies that the flag returns `true` for both Ready and Degraded states; callers needing stricter semantics must inspect the full enum.

### Extension Methods (Koan.Core.Adapters)

```csharp
namespace Koan.Core.Adapters;

/// <summary>
/// Extension methods for readiness-aware operations
/// </summary>
public static class AdapterReadinessExtensions
{
    /// <summary>
    /// Execute an operation with readiness gating based on adapter capabilities
    /// </summary>
    public static async Task<T> WithReadinessAsync<T>(
        this object adapter,
        Func<Task<T>> operation,
        CancellationToken ct = default)
    {
        if (adapter is not IAdapterReadiness readiness)
            return await operation();

        var config = adapter as IAdapterReadinessConfiguration;
        if (config?.EnableReadinessGating == false)
            return await operation();

        var policy = config?.Policy ?? ReadinessPolicy.Hold;

        switch (policy)
        {
            case ReadinessPolicy.Immediate:
                if (!await readiness.IsReadyAsync(ct))
                    throw new AdapterNotReadyException(
                        adapter.GetType().Name,
                        readiness.ReadinessState,
                        $"Adapter {adapter.GetType().Name} is not ready (State: {readiness.ReadinessState})");
                break;

            case ReadinessPolicy.Hold:
                await readiness.WaitForReadinessAsync(config?.Timeout, ct);
                break;

            case ReadinessPolicy.Degrade:
                // Continue - operations must surface adapter-provided degradation metadata
                break;
        }

        return await operation();
    }

    /// <summary>
    /// Execute a void operation with readiness gating
    /// </summary>
    public static async Task WithReadinessAsync(
        this object adapter,
        Func<Task> operation,
        CancellationToken ct = default)
    {
        await adapter.WithReadinessAsync(async () =>
        {
            await operation();
            return true;
        }, ct);
    }
}
```

### Framework Integration (Koan.Core.Adapters)

```csharp
namespace Koan.Core.Adapters;

/// <summary>
/// Hosted service that initializes all async adapters during application startup
/// </summary>
public class AdapterInitializationService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AdapterInitializationService> _logger;
    private readonly IReadOnlyCollection<IAdapterInitializationOrder> _orderingPolicies;
    private readonly IRetryPolicyProvider _retryPolicies;

    public AdapterInitializationService(
        IServiceProvider services,
        ILogger<AdapterInitializationService> logger,
        IEnumerable<IAdapterInitializationOrder> orderingPolicies,
        IRetryPolicyProvider retryPolicies)
    {
        _services = services;
        _logger = logger;
        _orderingPolicies = orderingPolicies.ToArray();
        _retryPolicies = retryPolicies;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var asyncInitializers = _services.GetServices<IAsyncAdapterInitializer>().ToList();

        if (!asyncInitializers.Any())
        {
            _logger.LogDebug("No async adapter initializers found");
            return;
        }

        _logger.LogInformation("Initializing {Count} async adapters", asyncInitializers.Count);

        foreach (var wave in ApplyOrdering(asyncInitializers))
        {
            _logger.LogDebug("Starting initialization wave containing {Count} adapters", wave.Count);

            var tasks = wave.Select(initializer => InitializeAdapterSafelyAsync(initializer, ct));
            await Task.WhenAll(tasks);
        }

        _logger.LogInformation("All async adapters initialized successfully");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task InitializeAdapterSafelyAsync(IAsyncAdapterInitializer initializer, CancellationToken ct)
    {
        var adapterType = initializer.GetType().Name;
        try
        {
            _logger.LogDebug("Initializing adapter: {AdapterType}", adapterType);

            var retryPolicy = _retryPolicies.GetPolicy(adapterType);

            await retryPolicy.ExecuteAsync(async innerCt =>
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(5)); // Global initialization timeout

                await initializer.InitializeAsync(timeoutCts.Token);
            }, ct);

            _logger.LogDebug("Successfully initialized adapter: {AdapterType}", adapterType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize adapter: {AdapterType}", adapterType);

            if (initializer is IAdapterReadiness readiness && readiness.ReadinessState != AdapterReadinessState.Failed)
            {
                _logger.LogWarning(
                    "Adapter {AdapterType} failed initialization but didn't update readiness state",
                    adapterType);
            }
        }
    }

    private IEnumerable<IReadOnlyCollection<IAsyncAdapterInitializer>> ApplyOrdering(
        IReadOnlyCollection<IAsyncAdapterInitializer> initializers)
    {
        if (!_orderingPolicies.Any())
            return new[] { initializers };

        var waves = new List<IReadOnlyCollection<IAsyncAdapterInitializer>>();
        var remaining = initializers;

        foreach (var policy in _orderingPolicies.OrderBy(p => p.Priority))
        {
            var slice = policy.Apply(remaining).ToArray();
            if (slice.Length > 0)
            {
                waves.Add(slice);
                remaining = remaining.Except(slice).ToArray();
            }
        }

        if (remaining.Any())
            waves.Add(remaining);

        return waves;
    }
}
```

### Observability & Monitoring

* Surface readiness through the existing health endpoint as a distinct `readiness` contributor containing state, duration, and last transition information per adapter.
* Emit structured logs on every `ReadinessStateManager.StateChanged` event so operators can correlate outages with deployment timelines.
* Publish metrics such as readiness latency histograms, degraded/failed counters, and retry attempts using the framework telemetry pipeline.

### Supporting Infrastructure Interfaces

* `IAdapterInitializationOrder` – Allows adapters to express dependencies or priorities so the initialization service can run them in deterministic waves.
* `IRetryPolicyProvider` – Supplies adapter-specific retry/backoff strategies (e.g., exponential, linear) that honor global startup budgets while tolerating transient failures.

## Implementation Plan

### Phase 1: High-Priority Adapters (Core + Primary External Resources)

**Target:** Adapters causing the most startup issues and console errors

**Core Infrastructure:**
- [ ] Add capability interfaces to `Koan.Core.Adapters`
- [ ] Implement `ReadinessStateManager` and `AdapterReadinessExtensions`
- [ ] Create `AdapterInitializationService` with ordering and retry hooks
- [ ] Add service registration in `Koan.Core` auto-registrar
- [ ] Add configuration support in `Koan.Core`
- [ ] Wire readiness signals into logging, metrics, and health endpoints

**Couchbase Implementation:**
- [ ] Implement `IAdapterReadiness` in `CouchbaseRepository<T,K>`
- [ ] Implement `IAsyncAdapterInitializer` in `CouchbaseClusterProvider`
- [ ] Update all repository operations to use `WithReadinessAsync()`
- [ ] Add configuration options for Couchbase readiness policies
- [ ] Update auto-registrar to register async initialization

**Ollama Implementation:**
- [ ] Implement `IAdapterReadiness` in `OllamaAdapter`
- [ ] Handle model downloading and service availability checks
- [ ] Update AI operations to wait for model readiness
- [ ] Add model-specific readiness policies
- [ ] Support required models configuration

**Mongo Implementation:**
- [ ] Implement readiness capabilities in `MongoRepository<T,K>`
- [ ] Handle connection establishment and database/collection creation
- [ ] Update operations to use readiness gating
- [ ] Add Mongo-specific readiness configuration and degraded behavior guidance

**Success Criteria for Phase 1:**
- ✅ **Zero console errors during application startup**
- ✅ **Clean initialization sequence with proper wait times**
- ✅ **Couchbase operations succeed after cluster readiness**
- ✅ **Ollama operations wait for required models**
- ✅ **Mongo operations wait for connection establishment**
- ✅ **Configurable policies work (Immediate/Hold/Degrade)**

### Phase 2: Remaining Adapters (Complete Framework Coverage)

**Target:** All remaining external resource adapters

**Weaviate Implementation:**
- [ ] Implement readiness in `WeaviateVectorRepository`
- [ ] Handle schema provisioning and connectivity verification
- [ ] Update vector operations to use readiness gating

**Storage.Local Implementation:**
- [ ] Implement readiness in local storage services
- [ ] Handle directory creation and permission verification
- [ ] Update storage operations to use readiness gating

**SQL Adapters (SqlServer, Postgres, Sqlite):**
- [ ] Implement readiness in SQL repositories
- [ ] Handle connection establishment and schema verification
- [ ] Update query operations to use readiness gating

**Redis Implementation:**
- [ ] Implement readiness in Redis data adapter
- [ ] Handle connection establishment and key space verification
- [ ] Update caching operations to use readiness gating

**Additional Adapters:**
- [ ] Any new adapters requiring external resource initialization
- [ ] Custom adapters following the readiness pattern

**Framework Integration & Documentation:**
- [ ] Update framework documentation with readiness patterns
- [ ] Create migration guide for custom adapters, including `ReadinessStateManager`
- [ ] Add readiness monitoring to health endpoints, logs, and metrics
- [ ] Performance testing and optimization
- [ ] Publish degraded-mode playbook for adapter authors

**Success Criteria for Phase 2:**
- ✅ **All framework adapters follow consistent readiness patterns**
- ✅ **No startup errors across any adapter type**
- ✅ **Universal configuration support**
- ✅ **Complete documentation and migration guides**

## Configuration Schema

```json
{
  "Koan": {
    "Adapters": {
      "Readiness": {
        "DefaultPolicy": "Hold",
        "DefaultTimeout": "00:00:30",
        "InitializationTimeout": "00:05:00",
        "EnableMonitoring": true
      }
    },
    "Data": {
      "Couchbase": {
        "Readiness": {
          "Policy": "Hold",
          "Timeout": "00:01:00",
          "EnableReadinessGating": true
        }
      },
      "Mongo": {
        "Readiness": {
          "Policy": "Hold",
          "Timeout": "00:00:30"
        }
      }
    },
    "AI": {
      "Ollama": {
        "Readiness": {
          "Policy": "Hold",
          "Timeout": "00:05:00",
          "RequiredModels": ["all-minilm", "llama2"]
        }
      }
    }
  }
}
```

Configuration values follow the deterministic hierarchy: framework defaults → package defaults → application overrides → environment overrides. Conflicts resolve closest to the consumer, and readiness policies fail fast when both global and adapter-specific timeouts are exceeded. Documentation will include tables illustrating how defaults cascade for common environments (local dev, CI, production).

## Expected Behavior Changes

### Before Implementation (Current Issues)

**Couchbase Console Errors:**
```
Cluster initialization failed with status Unauthorized
SocketNotAvailableException: MultiplexingConnection
Database fallback failed, returning demo data
```

**Ollama Model Issues:**
```
Model 'all-minilm' not found, attempting to pull...
AI operation failed: model not ready
Falling back to cached embeddings
```

**Mongo Connection Issues:**
```
MongoConnectionException: Unable to connect to server
Database operation timeout after 30s
Retrying connection attempt 3/5...
```

### After Implementation (Clean Startup)

**Couchbase:**
```
Initializing Couchbase cluster at couchbase:8091 with user KoanUser
Couchbase cluster initialization completed successfully
Couchbase repository for Media is ready
```

**Ollama:**
```
Checking Ollama model availability: all-minilm
All required models are available
Ollama adapter is ready for AI operations
```

**Mongo:**
```
Establishing MongoDB connection to mongodb://mongo:27017
MongoDB connection established successfully
Mongo repository is ready for data operations
```

**Overall:**
```
Initializing 3 async adapters
Successfully initialized adapter: CouchbaseClusterProvider
Successfully initialized adapter: OllamaAdapter
Successfully initialized adapter: MongoRepository
All async adapters initialized successfully
Application ready for requests
```

## Migration Strategy

### For Framework Maintainers
1. **Non-Breaking**: All existing adapters continue working without changes
2. **Phased Rollout**: Start with high-impact adapters (Phase 1), then complete coverage (Phase 2)
3. **Backward Compatibility**: Existing applications work unchanged during migration

### For Application Developers
1. **Zero Config**: Default policies provide good experience out-of-the-box
2. **Configuration Override**: Adjust policies per deployment environment
3. **Observability**: Monitor adapter readiness through health endpoints

### For Custom Adapter Authors
1. **Simple Implementation**: Three interfaces provide full readiness support
2. **Flexible Adoption**: Can implement just `IAdapterReadiness` for basic support
3. **Framework Integration**: Extension methods handle policy enforcement

## Success Metrics

### Developer Experience
- ✅ **Zero console errors during normal application startup**
- ✅ **Predictable initialization timing**
- ✅ **Clear error messages when adapters fail**
- ✅ **Configurable behavior for different deployment scenarios**

### Framework Consistency
- ✅ **Same readiness pattern across all external resource adapters**
- ✅ **Follows existing capability-based architecture**
- ✅ **Non-breaking enhancement to existing codebase**

### Operational Excellence
- ✅ **Proper separation of health (liveness) vs readiness (operational)**
- ✅ **Observable adapter states for monitoring**
- ✅ **Graceful degradation options**

## Technical Notes for Implementation

### Key Design Decisions
1. **Capability-Based**: Follows `IQueryCapabilities` pattern, not inheritance
2. **Adapter-Agnostic**: Works for Data, AI, Storage, Messaging adapters
3. **Policy-Driven**: Configurable behavior for different scenarios
4. **Extension Method Pattern**: Clean integration with existing operations
5. **API Ergonomics**: Provide helper wrappers for static/entity-first APIs so readiness gating is applied automatically

### Implementation Priorities
1. **Phase 1 Focus**: Couchbase (immediate console errors), Ollama (model loading), Mongo (connection issues)
2. **Startup Sequence**: Initialization before operations with ordered waves when dependencies exist
3. **Visibility First**: Emit readiness metrics/logs before enabling gating to aid rollout debugging
4. **Maintain Compatibility**: Existing code works unchanged while adapters gradually adopt readiness

### Degraded Mode Guidance
* Adapters entering `Degraded` must populate a scoped context (e.g., `ReadinessDegradationContext`) describing impacted capabilities.
* Entity and service operations should surface degradation metadata to callers through existing result envelopes or logging.
* Document adapter-specific behaviors (read-only Couchbase, cached embeddings for Ollama, limited indexing for Mongo) to set clear expectations.

### Testing Strategy
1. **Unit Tests**: Individual capability implementations, including `ReadinessStateManager` transitions
2. **Integration Tests**: Full startup sequence with Docker
3. **Performance Tests**: Initialization timing and parallel startup
4. **Compatibility Tests**: Existing applications continue working
5. **Resilience Tests**: Failure-injection scenarios (delayed services, transient network faults) to validate retries, degraded transitions, and monitoring signals

## Reference Implementation Examples

### Couchbase Repository Pattern

```csharp
public class CouchbaseRepository<T, K> : IDataRepository<T, K>,
    IAdapterReadiness, IAdapterReadinessConfiguration, IAsyncAdapterInitializer
    where T : class, IEntity<K> where K : notnull
{
    private readonly ReadinessStateManager _stateManager = new();

    // IAdapterReadiness Implementation
    public AdapterReadinessState ReadinessState => _stateManager.State;
    public bool IsReady => _stateManager.IsReady;
    public TimeSpan ReadinessTimeout => TimeSpan.FromSeconds(30);
    public ReadinessStateManager StateManager => _stateManager;
    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged;

    public CouchbaseRepository(...)
    {
        _stateManager.StateChanged += (_, args) => ReadinessStateChanged?.Invoke(this, args);
    }

    // Operations use readiness extensions
    public async Task<T?> GetAsync(K id, string? set = null, CancellationToken ct = default)
        => await this.WithReadinessAsync(async () =>
        {
            var context = await _clusterProvider.GetCollectionContextAsync(set ?? GetCollectionName(), ct);
            // ... existing implementation
        }, ct);

    // Async initialization
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await _clusterProvider.InitializeAsync(ct);
            _stateManager.TransitionTo(AdapterReadinessState.Ready);
        }
        catch (Exception)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            throw;
        }
    }
}
```

### Ollama Adapter Pattern

```csharp
public class OllamaAdapter : IAdapterReadiness, IAsyncAdapterInitializer
{
    private readonly string[] _requiredModels;
    private readonly ReadinessStateManager _stateManager = new();

    public AdapterReadinessState ReadinessState => _stateManager.State;
    public bool IsReady => _stateManager.IsReady;
    public TimeSpan ReadinessTimeout => TimeSpan.FromMinutes(5);
    public ReadinessStateManager StateManager => _stateManager;
    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged;

    public OllamaAdapter(...)
    {
        _stateManager.StateChanged += (_, args) => ReadinessStateChanged?.Invoke(this, args);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        foreach (var model in _requiredModels)
        {
            if (!await IsModelAvailableAsync(model, ct))
            {
                await PullModelAsync(model, ct);
            }
        }
        _stateManager.TransitionTo(AdapterReadinessState.Ready);
    }

    public async Task<EmbeddingResponse> GetEmbeddingsAsync(string text, CancellationToken ct = default)
        => await this.WithReadinessAsync(async () =>
        {
            // ... existing implementation
        }, ct);
}
```

---

**Next Steps:** Begin Phase 1 implementation with core infrastructure in `Koan.Core.Adapters`, then proceed with Couchbase, Ollama, and Mongo adapters to resolve the most critical startup issues.

## Implementation Context for Future Sessions

### Current State (as of session end)
- **Couchbase Console Errors**: Active issue with `SocketNotAvailableException` during startup
- **S5.Recs Sample**: Experiencing repeated connection failures in Docker environment
- **Root Cause**: Missing readiness system leads to operations executing before adapters are ready

### Key Files to Modify
- `src/Koan.Core.Adapters/` (new namespace for readiness capabilities)
- `src/Koan.Data.Connector.Couchbase/CouchbaseRepository.cs` (add readiness implementation)
- `src/Koan.Data.Connector.Couchbase/CouchbaseClusterProvider.cs` (add async initialization)
- `src/Koan.AI.Connector.Ollama/OllamaAdapter.cs` (add model readiness)
- `src/Koan.Data.Connector.Mongo/MongoRepository.cs` (add connection readiness)

### Success Criteria Verification
Test with `samples/S5.Recs` Docker environment:
```bash
cd samples/S5.Recs
./start.bat
# Should show clean startup logs without console errors
```

### Framework Principles to Maintain
- **"Reference = Intent"**: Auto-registration continues unchanged
- **Entity-First Development**: `Data<T>.All()` API remains the same
- **Provider Transparency**: Same code works across all storage backends
- **Capability-Based**: Use interfaces, not inheritance hierarchies

