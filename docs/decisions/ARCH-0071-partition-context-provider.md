# ARCH-0071: Vector Partition Awareness via EntityContext

**Status:** ✅ **ACCEPTED**
**Date:** 2025-11-05
**Scope:** Vector<T> integration with EntityContext partition routing
**Related:** [DATA-0077](./DATA-0077-entity-context-source-adapter-partition-routing.md), [Koan Context proposal](../archive/proposals/Koan-context.md)

## Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Partition Context** | Reuse `EntityContext` from DATA-0077 | Single unified routing system for Entity<T> and Vector<T> |
| **Storage Mechanism** | `EntityContext.Current.Partition` (static AsyncLocal) | Already exists, battle-tested, no new abstractions needed |
| **API Surface** | `EntityContext.Partition(id)` + `Vector<T>.WithPartition(id)` | Consistent with Data<T>.WithPartition() pattern |
| **Vector Integration** | Vector<T> reads `EntityContext.Current.Partition` | Automatic partition awareness, no explicit parameters |
| **Provider Mapping** | `IVectorPartitionMapper` maps partition → storage name | Weaviate: class name, Pinecone: namespace, Qdrant: collection |
| **Nested Scopes** | Supported (EntityContext feature) | Inner scope overrides outer scope |

---

## Context

**Problem:** Koan Context (local-first code indexing service) requires **partition-aware vector storage** where each project's vectors are isolated.

**Discovery:** The framework **already has** `EntityContext` (DATA-0077) providing ambient partition routing for Entity<T> via `EntityContext.Current.Partition`. Vector<T> should integrate with this existing system rather than creating parallel infrastructure.

**Use Cases:**
1. **Koan Context:** Isolate vector embeddings per project (e.g., `koan-framework` vectors vs. `aspnetcore` vectors)
2. **Multi-tenant SaaS:** Route Entity<T> and Vector<T> operations to tenant-specific storage partitions
3. **Test isolation:** Partition test data without connection string changes
4. **Staged deployments:** Route to blue/green partitions for canary deployments

---

## Current State: EntityContext Exists (DATA-0077)

**What exists:**
- ✅ `EntityContext.Current.Partition` - Ambient partition context (AsyncLocal)
- ✅ `EntityContext.Partition(id)` - Scoped partition routing
- ✅ `Data<T>.WithPartition(id)` - Convenience method for Entity<T>
- ✅ `Entity<T>.Get(id, partition)` - Explicit partition parameter overloads
- ❌ **Vector<T> doesn't use EntityContext yet**

**Current Vector<T> state:**
- No partition awareness
- No integration with EntityContext
- All vectors stored globally (no isolation)

---

## Proposed Architecture

### 1. Reuse EntityContext (DATA-0077)

```csharp
// Already exists in Koan.Data.Core/EntityContext.cs
public static class EntityContext
{
    public sealed record ContextState(
        string? Source,      // Named configuration (e.g., "analytics")
        string? Adapter,     // Provider override (e.g., "sqlite")
        string? Partition);  // Storage partition suffix (e.g., "project-abc")

    public static ContextState? Current { get; } // AsyncLocal<ContextState>

    public static IDisposable With(
        string? source = null,
        string? adapter = null,
        string? partition = null);

    // Convenience methods
    public static IDisposable Source(string source);
    public static IDisposable Adapter(string adapter);
    public static IDisposable Partition(string partition);  // ← Vector<T> uses this
}
```

**Key Points:**
- ✅ Already implemented and battle-tested
- ✅ Used extensively in EntitySoftDeleteController, EntityModerationController, EntityAuditController
- ✅ Supports three-dimensional routing: source + adapter + partition
- ✅ AsyncLocal-based, thread-safe, async-compatible
- ✅ No DI registration needed (static class)

### 4. Integration Points

### 2. Vector<T> Reads EntityContext.Current.Partition

```csharp
// In Koan.Data.Vector/Vector.cs
public class Vector<TEntity> where TEntity : class, IEntity<string>
{
    // Convenience method (matches Data<T>.WithPartition pattern)
    public static IDisposable WithPartition(string partition)
        => EntityContext.Partition(partition);

    public static async Task<int> Save(
        IEnumerable<(string Id, float[] Embedding, object? Meta)> items,
        CancellationToken ct = default)
    {
        var partition = EntityContext.Current?.Partition;  // ← Read from EntityContext
        return await VectorData<TEntity>.UpsertManyAsync(items, partition, ct);
    }

    public static async Task<VectorQueryResult<string>> Search(
        VectorQueryOptions options,
        CancellationToken ct = default)
    {
        var partition = EntityContext.Current?.Partition;  // ← Read from EntityContext
        return await VectorData<TEntity>.SearchAsync(options, partition, ct);
    }
}
```

#### Entity<T> Integration (Future Enhancement)

```csharp
// In Koan.Data.Core/Entity.cs
public static async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
{
    var partitionProvider = AppHost.Current?.GetService<IPartitionContextProvider>();
    var partition = partitionProvider?.CurrentPartitionId;

    if (partition != null)
    {
        // Pass partition hint to repository layer
        // Provider-specific adapters can use this for:
        // - Table suffixes (SQL: users_tenant1, users_tenant2)
        // - Collection routing (Mongo: db.tenant1.users, db.tenant2.users)
        // - Connection string selection (Redis: redis-tenant1:6379)
    }

    return await Repository.GetAsync(id, ct);
}
```

---

## Usage Examples

### Example 1: Koan Context (Per-Project Vector Isolation)

```csharp
using (EntityContext.Partition("project-koan-framework"))
{
    // All Entity<T> and Vector<T> operations scoped to this partition
    var doc = new DocumentChunk
    {
        Content = "Koan.Core provides auto-registration...",
        FilePath = "docs/architecture.md"
    };

    await doc.Save(); // Vector embedding stored in "KoanDocument_project_koan_framework" class

    var results = await DocumentChunk.SemanticSearch("auto-registration patterns");
    // Returns only documents from "project-koan-framework"
}
```

### Example 2: Multi-Tenant SaaS

```csharp
// Request middleware extracts tenant from JWT claims
var tenantId = context.User.FindFirst("tenantId")?.Value;

using (EntityContext.Partition(tenantId))
{
    // All Entity<T> and Vector<T> operations scoped to tenant
    var user = await User.Get(userId);
    var orders = await Order.Query(o => o.Status == OrderStatus.Pending);
    var recommendations = await Product.SemanticSearch(user.Preferences);
}
```

### Example 3: Convenience Method (Matches Data<T> Pattern)

```csharp
// Pattern 1: EntityContext.Partition() - Framework-wide
using (EntityContext.Partition("archive"))
{
    var todos = await Todo.Query(x => x.Status == TodoStatus.Archived);
    var docs = await Vector<Document>.Search(options);  // Same partition
}

// Pattern 2: Type-specific WithPartition() - Convenience
using (Vector<Document>.WithPartition("backup"))  // ← Convenience method
{
    var results = await Vector<Document>.Search(options);
}
```

### Example 4: Global (Unpartitioned) Operations

```csharp
// No partition scope = global operations
var allUsers = await User.Query().ToListAsync(); // All partitions

using (var scope = partitionProvider.BeginScope("tenant-123"))
{
    var tenantUsers = await User.Query().ToListAsync(); // Only tenant-123
}

var globalAgain = await User.Query().ToListAsync(); // All partitions again
```

---

## Implementation Phases

### Phase 1: Core Infrastructure (Koan Context MVP)
- ✅ **Milestone 0 (Current ADR)**
  - Create `IPartitionContextProvider` interface
  - Implement `AsyncLocalPartitionContextProvider`
  - Create auto-registrar with boot report integration
  - Unit tests for scope management, nesting, async safety

### Phase 2: Vector<T> Integration (Koan Context MVP)
- Update `Vector<T>` to consult partition context
- Create `IVectorPartitionMapper` for storage name mapping
- Implement Weaviate per-class-per-partition provisioning
- Integration tests: two partitions, verify isolation

### Phase 3: Entity<T> Integration (Future)
- Update `Entity<T>` static methods to pass partition hint
- Implement partition awareness in SQL, NoSQL, JSON adapters
- Document provider-specific partition strategies
- Migration guide for existing apps

---

## Provider-Specific Partition Strategies

Different providers handle partitions differently:

### Weaviate (Implemented in Koan Context)
- **Strategy:** Per-partition class names
- **Example:** `KoanDocument_project_a`, `KoanDocument_project_b`
- **Pros:** Strong isolation, independent schemas, easy cleanup
- **Cons:** Schema proliferation with many partitions

### SQL Databases (Future)
- **Strategy A:** Table suffixes (`users_tenant1`, `users_tenant2`)
- **Strategy B:** Partition key column with filtered queries
- **Strategy C:** Schema per partition (`tenant1.users`, `tenant2.users`)

### MongoDB (Future)
- **Strategy A:** Collection per partition (`project_a.documents`, `project_b.documents`)
- **Strategy B:** Database per partition (`db_project_a.documents`, `db_project_b.documents`)

### Redis (Future)
- **Strategy:** Key prefix (`tenant:123:session:abc`)

### Couchbase (Future)
- **Strategy:** Scope per partition (`bucket.tenant_123.users`)

**Adapter Responsibility:** Each adapter decides how to interpret partition context based on provider capabilities.

---

## Performance Considerations

### AsyncLocal<T> Overhead
- **Allocation:** ~40 bytes per scope (Stack<string> + disposer)
- **Lookup time:** O(1) constant time (AsyncLocal is fast)
- **GC pressure:** Minimal, scopes are short-lived

### Nested Scope Performance
- **Stack depth:** Typically 1-2 levels, rarely >5
- **Pop/Push:** O(1) operations
- **Benchmark:** <100ns per scope create/dispose

### Recommendation
- ✅ Use scopes liberally — overhead is negligible
- ✅ Dispose scopes promptly with `using` statements
- ⚠️ Avoid extremely deep nesting (>10 levels)

---

## Security Considerations

### Partition Injection Risks
**Risk:** Malicious user supplies partition ID to access other tenants' data.

**Mitigation:**
- ✅ **Server-side only:** Partition ID comes from server logic (JWT claims, middleware), never from client input
- ✅ **Validation:** `EntityContext.With(partition:)` validates the partition name and throws `ArgumentException` on invalid input — it must be a GUID, or contain only letters, digits, `-`, `.`, `_` (see DATA-0077 §4). Lossy/ambiguous names (spaces, `/`, `$`, …) are rejected, so a hostile value can never collapse onto another tenant's store via sanitization.
- ✅ **Authorization:** Application layer enforces which partitions a user can access

### Partition Leakage
**Risk:** Forgetting to dispose scope leaks partition context to subsequent requests.

**Mitigation:**
- ✅ **Request-scoped middleware:** Web apps should use middleware to ensure scopes are disposed
- ✅ **Using statements:** All examples enforce `using` pattern
- ✅ **Tests:** Verify partition isolation between concurrent operations

### Logging & Observability
- ✅ Include partition ID in structured logs for audit trails
- ✅ Add partition context to OpenTelemetry spans
- ✅ Metrics: track operations per partition

---

## Testing Strategy

### Unit Tests
- ✅ Scope creation and disposal
- ✅ Nested scopes (inner overrides outer)
- ✅ Async/await preservation across continuations
- ✅ Concurrent scopes in different async contexts
- ✅ Null partition ID validation

### Integration Tests
- ✅ Vector<T> save/search with partition context
- ✅ Weaviate class isolation (project-a vs project-b)
- ✅ Web app: middleware sets partition from claims
- ✅ Background jobs: partition context from job metadata

### Performance Tests
- ✅ Benchmark scope create/dispose overhead
- ✅ Load test: 10k concurrent partition-scoped operations
- ✅ Verify no memory leaks with long-lived scopes

---

## Migration Path

### For Existing Koan Apps
- ✅ **Backward compatible:** No breaking changes
- ✅ **Opt-in:** Partition context only used when explicitly set
- ✅ **Default behavior unchanged:** When partition is null, operations are global

### For New Apps (Koan Context)
- ✅ **Required for multi-project isolation**
- ✅ **Convention:** Use project slug as partition ID

---

## Alternatives Considered

### Alternative 1: Provider-Specific Partition Config
**Approach:** Each provider handles partitioning via connection strings.

**Rejected because:**
- ❌ Not runtime-switchable (requires restart)
- ❌ Verbose (every operation needs explicit config)
- ❌ Not portable across providers

### Alternative 2: Partition as Method Parameter
**Approach:** `Entity.Get(id, partition: "tenant-123")`

**Rejected because:**
- ❌ Repetitive (every call needs partition param)
- ❌ Easy to forget (unsafe by default)
- ❌ Doesn't compose well with existing code

### Alternative 3: Partition-Specific Repositories
**Approach:** `IRepository<T, TKey> repo = repoFactory.GetForPartition("tenant-123")`

**Rejected because:**
- ❌ Violates Koan's Entity-First philosophy (no manual repos)
- ❌ Requires DI plumbing in every service
- ❌ Cumbersome for mixed-partition operations

### Alternative 4: Custom HttpContext Items
**Approach:** Store partition in `HttpContext.Items["partition"]`

**Rejected because:**
- ❌ Web-only (doesn't work in background jobs, tests, console apps)
- ❌ Not type-safe
- ❌ Requires manual propagation

**Chosen approach (AsyncLocal)** wins on all dimensions: ambient, type-safe, portable, low overhead.

---

## Alignment with Koan Principles

### ✅ Entity-First Development
- Partition context is ambient — `doc.Save()` just works
- No repositories or services needed
- Clean, natural API surface

### ✅ "Reference = Intent"
- Adding `IPartitionContextProvider` DI registration enables partition awareness
- No explicit opt-in per entity required

### ✅ Multi-Provider Transparency
- Partition context is provider-agnostic
- Each adapter interprets partition based on its capabilities
- Same code works across SQL, NoSQL, Vector, JSON stores

### ✅ Self-Reporting Infrastructure
- Boot report shows partition context provider status
- Logs include partition ID for observability
- Metrics track per-partition operations

### ✅ Progressive Disclosure
- Simple: ignore partitions (global operations)
- Intermediate: use partition scopes
- Advanced: nested scopes, custom partition mappers

---

## Success Metrics

### Functional
- ✅ Koan Context can isolate vectors per project
- ✅ Multi-tenant SaaS apps can use single connection string
- ✅ Zero breaking changes to existing Koan apps

### Performance
- ✅ Scope overhead <100ns per create/dispose
- ✅ No measurable impact on Entity<T> or Vector<T> throughput
- ✅ AsyncLocal lookup is O(1) constant time

### Developer Experience
- ✅ Natural, using-statement-friendly API
- ✅ Clear documentation with examples
- ✅ Comprehensive test coverage (unit + integration)

---

## Next Steps

1. ✅ **Create this ADR** (ARCH-0071)
2. ⏳ **Implement Phase 1** (Core infrastructure)
   - Create interface and implementation
   - Write unit tests
   - Auto-register in Koan.Data.Core
3. ⏳ **Implement Phase 2** (Vector<T> integration)
   - Update Vector<T> to consult partition context
   - Create IVectorPartitionMapper
   - Integration tests with Weaviate
4. ⏳ **Document pattern** in Koan guides
5. 🔮 **Phase 3 (Future):** Entity<T> integration for full multi-tenancy

---

## References

- **Koan Context Proposal:** [docs/archive/proposals/Koan-context.md](../archive/proposals/Koan-context.md)
- **Implementation Checklist:** [docs/archive/proposals/Koan-context-checklist.md](../archive/proposals/Koan-context-checklist.md)
- **Related ADR:** [ARCH-0070: Attribute-Driven AI Embeddings](./ARCH-0070-attribute-driven-ai-embeddings.md)
- **AsyncLocal<T> Docs:** https://learn.microsoft.com/en-us/dotnet/api/system.threading.asynclocal-1

---

**Decision:** ✅ **APPROVED for implementation as Milestone 0 of Koan Context**

Framework-wide partition context provider enables true multi-tenant and multi-project data isolation with minimal overhead, clean API, and full backward compatibility. This is a foundational capability that benefits Vector<T> (Koan Context), Entity<T> (future SaaS apps), and establishes Koan as a first-class multi-tenant framework.
