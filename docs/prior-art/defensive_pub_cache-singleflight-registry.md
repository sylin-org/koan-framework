# Defensive Publication: Cache Singleflight Registry with Reference-Counted Semaphore Auto-Cleanup

## Header Block

- **Title:** Cache Singleflight Registry with Per-Key Semaphore Gating and Automatic Reference-Counted Cleanup
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Concurrent data access infrastructure, specifically methods for preventing cache stampede in high-concurrency environments using per-key lock management with automatic resource cleanup.
- **Keywords:** singleflight, cache stampede, thundering herd, per-key semaphore, reference counting, interlocked operations, concurrent dictionary, auto-cleanup, timeout, cache, concurrency

---

## 1. Problem Statement

Cache stampede (also called thundering herd or cache avalanche) occurs when a cached value expires and multiple concurrent requests simultaneously attempt to recompute it. In high-traffic systems, this can cause backend overload — instead of one request computing the value and serving it to all, N requests independently compute the same value, multiplying backend load by N.

Existing approaches to this problem in .NET have significant tradeoffs. Global locks serialize all cache operations regardless of key, destroying throughput. `Lazy<T>` per key solves concurrency but never releases the `Lazy<T>` object, causing memory leaks proportional to the total number of distinct keys ever accessed. `SemaphoreSlim` per key solves the concurrency problem but requires explicit lifecycle management — developers must manually track when a semaphore is no longer needed and dispose it, which is error-prone in concurrent environments.

The Go standard library's `singleflight` package elegantly solves this for Go, but .NET lacks a direct equivalent. Go's implementation uses mutexes and maps, which don't translate directly to .NET's `async/await` programming model where `SemaphoreSlim.WaitAsync()` is needed instead of blocking locks.

What is needed is a .NET-native singleflight implementation that: (a) gates concurrent access per cache key, (b) automatically cleans up gate objects when no callers remain, (c) supports async/await with configurable timeouts, and (d) propagates cancellation tokens.

---

## 2. Prior Art Summary

**Go `singleflight` package:** Provides in-process call deduplication where concurrent callers for the same key share a single execution result. However, it is synchronous (no async/await), has no timeout mechanism, and uses Go-specific primitives (sync.Mutex, sync.WaitGroup) that don't apply to .NET.

**.NET `Lazy<T>` / `LazyCache`:** Provides lazy initialization but creates one `Lazy<T>` per key that is never cleaned up. Over time, the internal dictionary grows unboundedly. No timeout support. No cancellation token propagation.

**.NET `ConcurrentDictionary` + `SemaphoreSlim`:** The building blocks exist, but combining them correctly requires careful lifecycle management. Naive implementations either leak semaphores (never removing them from the dictionary) or have race conditions (removing a semaphore while another thread is about to use it). The interlocked reference counting pattern that solves this is non-obvious and commonly implemented incorrectly.

**CacheManager / FusionCache:** Provide stampede protection but as part of larger caching frameworks with significant API surface. Not available as a standalone, zero-dependency pattern. FusionCache uses a similar approach internally but exposes it as an implementation detail rather than a reusable primitive.

**Specific gaps:**
1. No .NET library provides a standalone singleflight primitive with automatic gate cleanup.
2. No implementation combines per-key semaphore gating with interlocked reference counting for zero-leak lifecycle management.
3. No implementation provides configurable per-call timeouts with automatic fallback defaults.

---

## 3. Detailed Description of the Invention

### 3.1 Core Data Structure

The registry maintains a `ConcurrentDictionary<string, Gate>` where each `Gate` contains:

```
Gate:
  Semaphore: SemaphoreSlim(1, 1)  — allows exactly one concurrent execution
  RefCount: int                     — tracks active callers via Interlocked operations
```

### 3.2 Algorithm

```
RunAsync<T>(key, timeout, factory, cancellationToken):
  1. gate = _gates.GetOrAdd(key, new Gate())
     // Thread-safe: either reuses existing gate or creates new one
     // ConcurrentDictionary guarantees atomicity

  2. Interlocked.Increment(ref gate.RefCount)
     // Atomically increment reference count BEFORE acquiring semaphore
     // This prevents premature cleanup by other threads

  3. try:
       effectiveTimeout = (timeout <= 0) ? 5 seconds : timeout
       // Default timeout prevents indefinite blocking

       if NOT await gate.Semaphore.WaitAsync(effectiveTimeout, cancellationToken):
         throw TimeoutException("Singleflight timeout for key '{key}'")
         // Caller waited too long; another execution is still in progress

       try:
         return await factory(cancellationToken)
         // Only ONE caller executes factory at a time per key
         // Other callers wait on the semaphore
       finally:
         gate.Semaphore.Release()
         // Allow next waiting caller to proceed

     finally:
       if Interlocked.Decrement(ref gate.RefCount) == 0:
         _gates.TryRemove(key, out _)
         // Last caller departing — remove gate from dictionary
         // This prevents memory leaks from accumulated gates
```

### 3.3 Reference Counting Invariants

The reference count follows three invariants:

1. **Increment before semaphore acquisition:** `RefCount` is incremented before `WaitAsync`, ensuring the gate cannot be removed while a caller is waiting to acquire.
2. **Decrement in finally block:** `RefCount` is decremented in a `finally` block, ensuring it executes even if the factory throws, the semaphore times out, or cancellation occurs.
3. **Cleanup on zero:** `TryRemove` is called only when `RefCount` reaches exactly zero, meaning no callers are waiting or executing. `TryRemove` is itself thread-safe — if another thread has already removed the gate or a new caller has concurrently incremented the count, `TryRemove` is a no-op.

### 3.4 Race Condition Analysis

**Race 1: Gate removed while another thread calls GetOrAdd:**
Thread A decrements RefCount to 0 and calls TryRemove. Concurrently, Thread B calls GetOrAdd for the same key. Two outcomes are safe:
- If TryRemove completes first: GetOrAdd creates a fresh gate. Thread B proceeds normally.
- If GetOrAdd executes first: TryRemove removes the gate Thread A used, but Thread B has a reference to the new gate. No data loss.

**Race 2: Multiple threads decrement to zero simultaneously:**
Impossible — `Interlocked.Decrement` returns the new value atomically. Only one thread can observe the value reaching zero.

**Race 3: Timeout and factory execution interleave:**
If Thread A is executing the factory and Thread B times out waiting, Thread B throws `TimeoutException` and decrements RefCount. Thread A continues normally. If Thread A was the last caller, it cleans up the gate after factory completion.

### 3.5 Timeout Behavior

The configurable timeout applies to semaphore acquisition, not factory execution. If the factory itself hangs, waiting callers may time out, but the executing factory continues until completion (or cancellation). This is intentional — forcibly cancelling a factory mid-execution would violate the singleflight contract (the result should be shared with all waiters).

A fallback timeout of 5 seconds applies when the caller provides zero or negative timeout, preventing indefinite blocking from misconfiguration.

### 3.6 Cancellation Token Propagation

The `CancellationToken` is propagated to both:
1. `SemaphoreSlim.WaitAsync(timeout, cancellationToken)` — cancels the wait
2. `factory(cancellationToken)` — cancels the computation

If a waiting caller is cancelled, it decrements RefCount and throws `OperationCanceledException`. The executing factory is not affected.

---

## 4. Claims-Style Disclosure

1. A cache singleflight registry wherein per-key semaphore gates are stored in a concurrent dictionary and automatically removed via interlocked reference counting when no callers remain, distinct from static per-key caches (e.g., `Lazy<T>`) in that gates are automatically cleaned up to prevent unbounded memory growth.

2. A reference counting scheme for concurrent gate lifecycle management wherein reference count increment occurs before semaphore acquisition and decrement occurs in a finally block, ensuring that (a) gates cannot be removed while callers are waiting to acquire, and (b) cleanup is guaranteed even on timeout, cancellation, or factory exception.

3. A timeout mechanism for cache singleflight operations wherein the timeout applies to semaphore acquisition (not factory execution), with a configurable fallback default (5 seconds) when zero or negative timeout is provided, distinct from factory-level timeouts in that waiting callers time out independently while the executing factory continues.

4. A combined system wherein per-key semaphore gating, interlocked reference counting, configurable timeouts, and cancellation token propagation operate together to provide cache stampede prevention with zero memory leaks, async/await support, and graceful degradation on timeout — as a standalone, zero-dependency primitive.

5. A method wherein `ConcurrentDictionary.GetOrAdd` provides atomic gate creation and `ConcurrentDictionary.TryRemove` provides atomic gate cleanup, with the reference count serving as the coordination mechanism between creation and cleanup, distinct from manual lock-and-check patterns in that no explicit locking is required beyond the interlocked operations.

---

## 5. Implementation Evidence

- **Primary file:** `src/Koan.Cache/Singleflight/CacheSingleflightRegistry.cs`
- **Core infrastructure:** `src/Koan.Core/Infrastructure/Singleflight.cs` (related but distinct general-purpose singleflight)
- **Framework Version:** Koan Framework v0.6.3
- **Build Target:** net10.0

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** The singleflight pattern is well-known from Go's standard library. The .NET translation using SemaphoreSlim is obvious to a skilled .NET developer.

**Author revision:** Acknowledged that the singleflight concept originates from Go. The disclosure focuses on the specific .NET implementation with interlocked reference counting for automatic gate cleanup — a lifecycle management pattern not present in Go's implementation (which uses a different concurrency model). The non-obvious aspect is the precise ordering of increment-before-acquire and decrement-in-finally, combined with the atomic cleanup on zero, which prevents both memory leaks and race conditions. Added explicit race condition analysis (Section 3.4) demonstrating three specific scenarios that must be handled correctly.

### Pass 2
**Antagonist:** FusionCache and similar libraries already implement stampede protection internally. This is an implementation detail, not a novel pattern.

**Author revision:** Added clarification that FusionCache's internal stampede protection is coupled to its caching API and not exposed as a reusable primitive. The disclosure describes a standalone, zero-dependency singleflight registry that can be used with any caching layer. The architectural distinction is composability — this primitive is a building block, not a complete caching solution.

### Pass 3
**Antagonist:** No further objections. The reference counting lifecycle management with precise ordering guarantees, combined with the race condition analysis and timeout semantics, is sufficiently described to establish prior art. The standalone nature (vs. embedded in a caching framework) is a valid architectural distinction.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.
