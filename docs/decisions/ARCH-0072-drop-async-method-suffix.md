# ARCH-0072: Drop the Async Method Name Suffix

**Status**: Accepted
**Date**: 2026-03-24
**Deciders**: Enterprise Architect
**Scope**: All Koan-owned method declarations and call sites across src/, tests/, samples/

---

## Context

The .NET Task-based Asynchronous Pattern (TAP) guidelines from 2012 recommend suffixing async
methods with `Async` to distinguish them from synchronous overloads. This convention predates
`async/await` becoming the universal default and was designed for a world where most methods were
synchronous and a few were async.

Koan Framework exists in the opposite world:

1. **Every I/O method is async.** There are zero synchronous overloads of `Get`, `Upsert`,
   `Delete`, `Query`, `Search`, `Write`, `Publish`, `Chat`, `Embed`, or any other I/O operation.
   The suffix distinguishes nothing.

2. **The Entity-First API already omits it.** The most visible, most-used surface in the framework
   uses suffix-free names and has done so since v0.1:

   ```csharp
   await Todo.Get(id);        // not GetAsync
   await todo.Save();         // not SaveAsync
   await todo.Remove();       // not RemoveAsync
   await Todo.All();          // not AllAsync
   await Todo.Query(x => …);  // not QueryAsync
   ```

   No developer has been confused. The `await` keyword, `Task<T>` return type, and
   `CancellationToken ct` parameter already communicate async intent.

3. **The AI lifecycle facades already omit it.** `Agent.Create().Run()`, `Chain.Create().Run()`,
   `Eval.Measure()`, `Model.Pull()`, `Training.Train()` — all designed suffix-free from the start.

4. **The infrastructure layer contradicts the public layer.** `IDataRepository<T,K>.GetAsync()` is
   called by `Entity<T>.Get()`. The suffix lives in the middle of the stack for no consumer-visible
   reason, adding noise to every interface, implementation, and test.

### Scale of the Problem

| Layer | Async-suffixed methods | Call sites |
|-------|----------------------:|----------:|
| Interface contracts | ~60 | — |
| Implementation classes | ~420 | ~1,900 |
| Connectors (16 projects) | ~100 | — |
| Tests | ~120 declarations | ~190 |
| Samples | ~126 declarations | ~89 |
| **Total** | **~830 declarations** | **~2,200 call sites** |

### Prior Art

- **Entity Framework Core 8+**: Internal discussion to drop the suffix (blocked by backward compat)
- **System.Threading.Channels**: No `Async` suffix on `ReadAsync` / `WriteAsync` — wait, they do.
  But `ChannelReader<T>.ReadAllAsync()` returns `IAsyncEnumerable` without the suffix on the
  enumerable pattern.
- **Koan's own Entity<T>**: Proven since v0.1 that suffix-free works without confusion.
- **Go, Rust, Python, Kotlin, Swift**: No language outside .NET uses this convention. Async is the
  default everywhere else.

---

## Decision

**Remove the `Async` suffix from all Koan-owned method names.** Every method that currently ends in
`Async` will be renamed to its base verb form:

| Before | After |
|--------|-------|
| `GetAsync(id, ct)` | `Get(id, ct)` |
| `UpsertAsync(entity, ct)` | `Upsert(entity, ct)` |
| `DeleteAsync(id, ct)` | `Delete(id, ct)` |
| `QueryAsync(predicate, ct)` | `Query(predicate, ct)` |
| `CountAsync(query, ct)` | `Count(query, ct)` |
| `PatchAsync(id, patch, ct)` | `Patch(id, patch, ct)` |
| `SearchAsync(query, ct)` | `Search(query, ct)` |
| `WriteAsync(container, key, ct)` | `Write(container, key, ct)` |
| `ChatAsync(request, ct)` | `Chat(request, ct)` |
| `EmbedAsync(text, ct)` | `Embed(text, ct)` |
| `PromptAsync(request, ct)` | `Prompt(request, ct)` |
| `StreamAsync(request, ct)` | `Stream(request, ct)` |
| `SaveAsync(options, ct)` | `Save(options, ct)` |
| `PublishAsync(message, ct)` | `Publish(message, ct)` |
| `BindStoneAsync(options)` | `BindStone(options)` |
| `ResolveAsync(request)` | `Resolve(request)` |
| … | … |

### Exclusion List (NOT renamed)

Methods mandated by .NET base classes or interfaces that Koan does not own:

| Method | Required by |
|--------|-------------|
| `DisposeAsync()` | `IAsyncDisposable` |
| `ExecuteAsync(ct)` | `BackgroundService` |
| `StartAsync(ct)` | `IHostedService` |
| `StopAsync(ct)` | `IHostedService` |
| `OnConnectedAsync()` | `Hub` (SignalR) |
| `OnDisconnectedAsync(ex)` | `Hub` (SignalR) |
| `InvokeAsync(context, next)` | ASP.NET middleware convention |
| `InitializeAsync()` | xUnit `IAsyncLifetime` |
| `ConfigureAwait(bool)` | .NET BCL (not a suffix, but similar pattern) |

### Execution Strategy

**Global mass-replace + fix exemptions.** A single regex pass is cheaper than ~40 targeted
per-method patterns. The exemption list is small and well-defined.

```
Regex:  Async(        →  (
Scope:  all .cs files in src/, tests/, samples/
Guard:  anchored to '(' to avoid hitting type names (IAsyncEnumerable, AsyncLocal, etc.)
```

Steps:

1. Commit documentation baseline (done: `a14f3e57`)
2. Commit this ADR (done: `2f71cc75`)
3. **Global `sed s/Async(/(/g`** on all `.cs` files (excluding `obj/`, `bin/`)
4. **Restore exemptions**: grep for broken .NET-mandated method names, restore `Async` suffix
5. **Compile** (`dotnet build`) — compiler surfaces any remaining issues
6. **Fix stragglers**: string literals, `nameof()`, expression trees, reflection
7. **Run tests** — verify behavioural correctness
8. Commit as single atomic change

### Naming Collisions

Where a suffix-free overload already exists (e.g., `Entity<T>.Get(id)` wrapping
`IDataRepository.GetAsync(id)`), the wrapper's implementation simply calls the now-identically-named
interface method. The wrapper method's existence is unaffected — it is a static convenience method
on a different type.

No public API collision has been identified. The entity static methods (`Todo.Get`, `todo.Save`)
delegate to repository instance methods on a different type, so the names can match.

---

## Consequences

### Positive

- **Consistency**: Entity API, AI facades, and infrastructure layer all use the same convention
- **Reduced noise**: ~830 method names shortened; ~2,200 call sites cleaned up
- **Alignment with framework philosophy**: Koan is async-by-default; the suffix communicated nothing
- **Simpler onboarding**: New developers don't have to learn when to add/omit the suffix
- **Cross-language alignment**: Go, Rust, Python, Kotlin developers joining .NET see familiar naming

### Negative

- **One-time migration cost**: ~3,000 touch points in a single commit
- **Divergence from .NET BCL convention**: Koan code will look different from `HttpClient.GetAsync()`
  — but it already does via `Entity<T>.Get()`, so this is not a new divergence
- **External consumers**: Any code outside this repo that references Koan interfaces will break at
  compile time on upgrade. This is acceptable for a v0.x framework in early development.

### Risks

- **Reflection/string-based references**: Code that references method names as strings (e.g.,
  `nameof(GetAsync)`, MCP tool registration, expression trees) must be found and updated. The
  compiler will catch `nameof()` but not raw strings.
- **Serialized method names**: If any method names are serialized in persistent storage (unlikely for
  method names, but possible in MCP tool manifests), those must be updated.

### Mitigations

- Compiler-driven: The rename is a compile error everywhere it's missed
- `nameof()` usages will fail at compile time
- Grep for string literals containing old method names post-rename
- Full test suite run validates behavioral correctness

---

## References

- ARCH-0068: Refactoring Strategy — established the static-vs-DI decision framework
- Entity-First Development pattern — suffix-free since v0.1
- AI-0021: Category-Driven AI — designed suffix-free facades from inception
- Microsoft TAP Guidelines (2012): https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/task-asynchronous-programming-model
