# CORE-0003: Initializer Idempotency and Multi-Container Support

**Status**: Accepted
**Date**: 2025-10-03
**Deciders**: Enterprise Architecture Team

## Context

The Koan Framework's auto-registration system (`IKoanInitializer`, `AppBootstrapper`) uses a global singleton `InitializerRegistry` to prevent duplicate initialization. This design has a fundamental flaw:

### The Problem

```csharp
// Global AppDomain-scoped registry
public sealed class InitializerRegistry
{
    public static InitializerRegistry Instance { get; } = new();  // ← SINGLETON

    public bool TryRegisterInitializer(Type initializerType)
    {
        return _invokedInitializers.Add(key);  // Returns false if already exists
    }
}

// AppBootstrapper uses it
foreach (var initializerType in discoveredTypes)
{
    if (!registry.TryRegisterInitializer(initializerType))
    {
        continue;  // ← SKIPS for different ServiceCollection!
    }
    init.Initialize(services);
}
```

**Registry scope**: AppDomain (static singleton)
**ServiceCollection scope**: Instance (per-container)

**Result**: First ServiceCollection gets all initializers, subsequent ServiceCollections get NONE.

### Violates "Reference = Intent"

```csharp
// Test 1
var services1 = new ServiceCollection();
services1.AddKoan();  // ✅ InMemory adapter registered

// Test 2 (same AppDomain)
var services2 = new ServiceCollection();
services2.AddKoan();  // ❌ InMemory adapter NOT registered (blocked by registry)
```

This breaks:
- **Unit tests** (multiple ServiceCollections)
- **Integration tests** (container recreation)
- **Multi-tenant apps** (isolated containers)
- **Framework principle** ("Reference = Intent")

### Root Cause Analysis

The framework conflates two different initialization patterns:

1. **AppDomain-scoped static state** (MongoDB BsonSerializer, RecipeRegistry)
   - Must run once per process
   - Global registry is correct here

2. **ServiceCollection-scoped DI services** (Data adapters, providers)
   - Must run per container
   - Global registry is WRONG here

## Decision

**Remove the global `InitializerRegistry` and mandate idempotency at the initializer level.**

### Core Principles

1. **Always run initializers** for every `AddKoan()` call
2. **Each initializer manages its own idempotency** for static state
3. **ServiceCollection operations are naturally idempotent** (TryAdd, AddSingleton)

### Implementation Pattern

```csharp
public class SomeAutoRegistrar : IKoanAutoRegistrar
{
    // Per-initializer AppDomain guard for static operations
    private static bool _staticSetupComplete;

    public void Initialize(IServiceCollection services)
    {
        // Level 1: AppDomain-scoped static state (run once)
        if (!_staticSetupComplete)
        {
            lock (typeof(SomeAutoRegistrar))
            {
                if (!_staticSetupComplete)
                {
                    try
                    {
                        // Static registrations (MongoDB, etc.)
                        BsonSerializer.RegisterSerializer(...);
                        ConventionRegistry.Register(...);
                    }
                    catch (Exception ex) when (IsExpectedDuplicateError(ex))
                    {
                        // Already registered - safe to ignore
                    }
                    _staticSetupComplete = true;
                }
            }
        }

        // Level 2: ServiceCollection-scoped services (run every time)
        services.AddSingleton<IDataAdapterFactory, SomeAdapter>();
        services.TryAddSingleton<ISomeService, SomeService>();
        // ↑ ServiceCollection handles duplicates naturally
    }
}
```

### Updated AppBootstrapper

```csharp
public static void InitializeModules(IServiceCollection services)
{
    var assemblies = DiscoverAssemblies();

    foreach (var asm in assemblies)
    {
        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract || !typeof(IKoanInitializer).IsAssignableFrom(type))
                continue;

            try
            {
                var init = (IKoanInitializer)Activator.CreateInstance(type);
                init.Initialize(services);  // ← ALWAYS call, no registry check
            }
            catch { /* best effort */ }
        }
    }
}
```

## Consequences

### Benefits

1. **"Reference = Intent" Restored**
   - Package reference + `AddKoan()` = guaranteed registration
   - Works in all scenarios (tests, multi-tenant, etc.)

2. **Architectural Simplicity**
   - No global state tracking
   - Clear responsibility: each initializer owns its idempotency
   - Easier to reason about and debug

3. **Natural ServiceCollection Behavior**
   - `TryAdd` methods prevent duplicates
   - Last-wins for `AddSingleton` with same implementation
   - Framework works with DI container semantics

4. **Multi-Container Support**
   - Each container gets proper initialization
   - Test isolation guaranteed
   - Multi-tenant scenarios supported

### Risks & Mitigations

#### Risk 1: Static State Duplication
**Mitigation**: Per-initializer guards with try-catch for expected errors

```csharp
try
{
    BsonSerializer.RegisterSerializer(...);
}
catch (BsonSerializationException)
{
    // Already registered - safe to ignore
}
```

#### Risk 2: Performance (Multiple Initialize Calls)
**Mitigation**:
- Static guards are O(1) boolean checks
- ServiceCollection operations are fast (dictionary lookups)
- Negligible overhead vs. container creation cost

#### Risk 3: Migration Complexity
**Mitigation**: Phased rollout with clear guidelines

## Implementation Plan

### Phase 1: Core Infrastructure (This ADR)

1. **Update AppBootstrapper**
   - Remove `InitializerRegistry` usage
   - Always run initializers

2. **Deprecate InitializerRegistry**
   - Mark as `[Obsolete]`
   - Keep implementation for one version (safety)

### Phase 2: Update Initializers (Priority Order)

1. **Critical Path** (breaks without guards)
   - MongoDB adapters (BsonSerializer throws on duplicate)
   - Couchbase adapters (similar serialization registration)

2. **Low Risk** (already idempotent or no static state)
   - InMemory adapter (DI services only)
   - Recipe system (Contains check already present)
   - Messaging (dictionary assignment idempotent)

3. **Documentation Updates**
   - Initializer implementation guidelines
   - Migration guide for custom initializers

### Phase 3: Testing & Validation

1. **Automated Tests**
   - Multi-container test suite
   - Static state verification tests
   - Performance benchmarks

2. **Sample Apps**
   - Verify all samples work
   - Multi-tenant example

3. **Breaking Change Notice**
   - Release notes
   - Migration guide
   - Deprecation timeline

## Initializer Implementation Guidelines

### Pattern A: ServiceCollection Only (No Static State)

```csharp
public class SimpleAutoRegistrar : IKoanAutoRegistrar
{
    public void Initialize(IServiceCollection services)
    {
        // Just register services - naturally idempotent
        services.AddSingleton<IDataAdapterFactory, MyAdapter>();
        services.TryAddSingleton<IMyService, MyService>();
    }
}
```

### Pattern B: Static State + ServiceCollection

```csharp
public class ComplexAutoRegistrar : IKoanAutoRegistrar
{
    private static bool _staticInitialized;

    public void Initialize(IServiceCollection services)
    {
        // Static setup (once per AppDomain)
        if (!_staticInitialized)
        {
            lock (typeof(ComplexAutoRegistrar))
            {
                if (!_staticInitialized)
                {
                    InitializeStaticState();
                    _staticInitialized = true;
                }
            }
        }

        // Service registration (every call)
        services.AddSingleton<IMyFactory, MyFactory>();
    }

    private static void InitializeStaticState()
    {
        try
        {
            // Register serializers, conventions, etc.
            SomeLibrary.RegisterSerializer(...);
        }
        catch (Exception ex) when (IsKnownDuplicateError(ex))
        {
            // Already registered - safe to ignore
        }
    }
}
```

### Pattern C: Inherently Idempotent Static Registration

```csharp
public class RegistryAutoRegistrar : IKoanAutoRegistrar
{
    public void Initialize(IServiceCollection services)
    {
        // Static registry with Contains check - inherently idempotent
        foreach (var type in DiscoverTypes())
        {
            MyStaticRegistry.Register(type);
            // ↑ Contains internal: if (!_items.Contains(t)) _items.Add(t);
        }

        services.AddSingleton<IMyService, MyService>();
    }
}
```

## Related Decisions

- **DATA-0081**: InMemory Adapter Implementation (triggered this ADR)
- **Future**: May formalize initialization scopes with attributes

## References

- Issue: Multi-container test failures with InMemory adapter
- Root cause: Global InitializerRegistry blocking per-container registration
- Framework principle: "Reference = Intent" must work universally
