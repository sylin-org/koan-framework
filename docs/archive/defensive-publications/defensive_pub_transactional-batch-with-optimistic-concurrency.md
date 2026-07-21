# Defensive Publication: Provider-Agnostic Transactional Batch with Explicit Atomicity Contracts and Optimistic Concurrency

## Header Block

- **Title:** Provider-Agnostic Transactional Batch Operations with Explicit Atomicity Contracts and Optional Optimistic Concurrency Token Advancement
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Multi-provider data access frameworks, specifically methods for accumulating multiple entity operations into a batch with declarative atomicity requirements and provider-capability-aware execution.
- **Keywords:** batch operations, transactional batch, atomicity contract, optimistic concurrency, concurrency token, provider-agnostic, fluent accumulator, idempotency key, partial success, multi-provider

---

## 1. Problem Statement

Application frameworks supporting multiple data providers face a fundamental tension in batch operations. Relational databases support ACID transactions, making atomic batch execution straightforward. Document databases may support multi-document transactions with limitations. Key-value stores and file-based storage typically have no transaction support at all.

Existing frameworks either force all providers to support transactions (excluding simpler stores) or provide no batch abstraction at all (leaving coordination to developers). Entity Framework Core's `SaveChanges()` provides implicit batching but only for a single provider, with no explicit atomicity contract — developers cannot declare whether partial success is acceptable.

Additionally, optimistic concurrency control varies by provider. EF Core tracks concurrency via `ChangeTracker` (tightly coupled to DbContext), while document databases use ETags or version fields. No framework provides a provider-agnostic concurrency token mechanism that works across heterogeneous storage backends.

---

## 2. Prior Art Summary

**EF Core SaveChanges():** Implicit batch within a single DbContext. All changes are saved atomically or not at all. No explicit opt-in/opt-out of atomicity. Single provider only. Concurrency via ChangeTracker (tightly coupled).

**MongoDB BulkWrite:** Provider-specific batch API. Supports ordered/unordered execution but not cross-collection atomicity (prior to v4.0 multi-document transactions). No integration with entity-level framework.

**Dapper:** No batch abstraction. Developers manually loop or use multi-statement SQL.

**Spring Data:** `saveAll()` iterates individually — no atomicity guarantees. No explicit atomicity contract.

**Specific gaps:**
1. No framework provides an explicit `RequireAtomic` flag that declares atomicity requirements per batch.
2. No framework supports capability-aware execution: atomic when possible, best-effort when not.
3. No framework provides a provider-agnostic `[ConcurrencyToken]` attribute with automatic token advancement.

---

## 3. Detailed Description of the Invention

### 3.1 Fluent Batch Accumulator

```
IBatchSet<TEntity, TKey>:
  Add(entity)         — mark entity for insertion
  Update(entity)      — mark entity for update
  Update(id, mutate)  — mark entity for mutation via delegate
  Delete(id)          — mark entity for deletion
  Clear()             — reset accumulated operations
  Save(options?, ct)  — execute all accumulated operations
```

Operations are accumulated in memory without executing. `Save()` triggers execution with the specified options.

### 3.2 Atomicity Contract

```
BatchOptions:
  RequireAtomic: bool (default false)
  IdempotencyKey: string? (optional dedup key)
  MaxItems: int? (optional batch size limit)
```

**RequireAtomic = true:**
- Provider MUST execute all operations atomically (within a transaction)
- If the provider cannot support transactions: throws `NotSupportedException`
- On any failure: all operations roll back — no partial effects
- This is a hard requirement, not a preference

**RequireAtomic = false (default):**
- Provider executes operations with best-effort semantics
- Partial success is acceptable and tracked
- Provider may use a transaction if available, but is not required to
- Each operation can succeed or fail independently

### 3.3 Batch Result

```
BatchResult:
  Added: int      — count of successfully inserted entities
  Updated: int    — count of successfully updated entities
  Deleted: int    — count of successfully deleted entities
```

When `RequireAtomic = false`, the result reflects the actual outcomes (which may differ from the requested operations if some failed).

### 3.4 Optimistic Concurrency Token

```
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConcurrencyTokenAttribute : Attribute { }

// Entity example:
public class Product : Entity<Product, Guid>
{
    public string Name { get; set; }

    [ConcurrencyToken]
    public int Version { get; set; }
}
```

**Token behavior:**
- On upsert: provider includes `WHERE Version = @expectedVersion` (or equivalent)
- On successful upsert: Version is incremented (integer) or recomputed (ETag/hash)
- On version mismatch: operation returns conflict result without applying changes
- Token advancement is automatic — developers don't manually increment

**Provider implementation:**
- Relational (SQL): `UPDATE ... WHERE Id = @id AND Version = @version`; rows affected = 0 indicates conflict
- Document (MongoDB): Conditional update with version match
- Key-value: Check-and-set (CAS) if supported; no concurrency token if not supported

### 3.5 Idempotency Key

The optional `IdempotencyKey` enables retry safety. If a batch with the same key was already successfully executed, subsequent attempts are no-ops returning the original result. Implementation is provider-specific (may use a separate tracking table or built-in dedup).

### 3.6 Integration with Entity<T> Surface

```
// Batch via static method
var batch = Product.Batch()
    .Add(newProduct)
    .Update(existingProduct)
    .Delete(obsoleteId);

await batch.Save(new BatchOptions(RequireAtomic: true));

// Batch via collection extension
var products = new[] { p1, p2, p3 };
await products.Save(); // Convenience for batch upsert
```

---

## 4. Claims-Style Disclosure

1. A fluent batch accumulator for entity operations wherein `Add`, `Update`, and `Delete` operations are accumulated in memory and executed together via `Save()`, with execution behavior controlled by an explicit `BatchOptions` parameter, distinct from implicit batching (like EF Core SaveChanges) in that atomicity is a declared requirement, not an assumed behavior.

2. An explicit atomicity contract via `RequireAtomic` flag wherein `true` requires the provider to use a transaction (throwing `NotSupportedException` if transactions are unsupported) and `false` permits best-effort execution with partial success, distinct from all-or-nothing batch APIs in that the caller explicitly opts into or out of atomicity guarantees.

3. A provider-agnostic optimistic concurrency token mechanism via `[ConcurrencyToken]` attribute on entity properties, with automatic token advancement (integer increment or ETag recomputation) on successful persistence, distinct from ORM-specific concurrency tracking (like EF Core's ChangeTracker) in that the mechanism works across heterogeneous storage providers.

4. A conflict detection pattern wherein a version mismatch during concurrency-controlled upsert returns a conflict result without applying changes, enabling the caller to retry with a fresh entity version, distinct from exception-based concurrency failure in that conflicts are reported as data (not exceptions).

5. An idempotency key mechanism for batch operations wherein the same batch can be safely retried after transient failures, with the key enabling providers to detect and skip duplicate executions.

6. A batch size limiting mechanism via `MaxItems` that constrains the accumulator, preventing unbounded memory consumption from very large batches.

---

## 5. Implementation Evidence

- **Interface:** `IBatchSet<TEntity, TKey>` in `src/Koan.Data.Abstractions/IBatchSet.cs`
- **Options:** `BatchOptions` in `src/Koan.Data.Abstractions/BatchOptions.cs`
- **Result:** `BatchResult` in `src/Koan.Data.Abstractions/BatchResult.cs`
- **Extensions:** `BatchExtensions` in `src/Koan.Data.Core/BatchExtensions.cs`
- **ADR:** `docs/decisions/DATA-0007-transactional-batch-and-concurrency.md`
- **Framework Version:** Koan Framework v0.6.3

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** The `RequireAtomic` flag is trivially a boolean switch on whether to use a transaction. Any developer would think of this.

**Author revision:** The individual flag is simple. The inventive contribution is the contract semantics: when `RequireAtomic = true`, the provider MUST throw `NotSupportedException` if it cannot guarantee atomicity. This makes the atomicity requirement enforceable at the type system level — the caller knows at development time (via exception) whether their provider supports their requirements, rather than discovering partial failures in production.

### Pass 2
**Antagonist:** Optimistic concurrency with version columns is well-known. The `[ConcurrencyToken]` attribute is equivalent to EF Core's `[ConcurrencyCheck]`.

**Author revision:** EF Core's concurrency check is tied to ChangeTracker and DbContext. The disclosure describes a provider-agnostic mechanism where the same `[ConcurrencyToken]` attribute works across SQL, MongoDB, and any adapter that supports conditional writes. The provider implements the mechanism using its native capabilities (SQL WHERE clause, MongoDB conditional update, CAS operations). The automatic token advancement is also provider-specific but transparent to the application. Added emphasis that the novelty is provider-agnostic portability, not the concurrency concept itself.

### Pass 3
**Antagonist:** No further objections. The explicit atomicity contract with enforced capability checking, combined with provider-agnostic concurrency tokens, is sufficiently described.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.
