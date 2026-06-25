---
type: ARCHITECTURE
domain: storage
title: "STOR-0011 implementation plan (v2) — delegation prompts"
audience: [ai-agents, implementers]
status: active
last_updated: 2026-06-24
---

# STOR-0011 implementation plan (v2) — delegation prompts

> Gap C step 0.4 (storage blob-key axis isolation). Design = [STOR-0011](../decisions/STOR-0011-storage-blob-key-axis-isolation.md)
> **v2** (chokepoint at `IStorageService`, after design review `wf_ac5a1e07-54a` rejected the v1 `StorageEntity`
> funnel). Hand each TASK to an implementer **verbatim**, prepending the **Shared Context**. Order matters; each
> ends in a green commit. **Implementers MUST NOT redesign.** TASK 1–2 are intricate (a decorator + an ambient
> carrier + the logical/physical-key discipline) — assign them to a **capable** model and review the diff; TASK 3–5
> are mechanical.

---

## Shared Context (PREPEND TO EVERY TASK PROMPT)

You are implementing one task in the Koan framework (C#/.NET 10), repo root
`f:/Replica/NAS/Files/repo/github/sylin-org/koan-framework`, branch `dev`. **Do NOT push. Do NOT redesign — follow
the task and the ADR exactly.** Newtonsoft is canonical JSON. Build/test via the Bash/PowerShell tools.

**The feature (STOR-0011 v2):** isolate blobs by the registered data axes (today: tenant `__koan_tenant`) at the
**`IStorageService` boundary** — the one chokepoint every blob path funnels through (`StorageEntity<T>`,
`MediaEntity`, `IMediaSource`/`MediaController`, presigned URLs, the extension helpers, transfer, list). Reuse the
EXISTING seams; invent no new ones:

- **`ManagedFieldRegistry`** (`src/Koan.Data.Abstractions/Pipeline/ManagedFieldRegistry.cs`): `bool IsEmpty`;
  `IReadOnlyList<ManagedFieldDescriptor> ForType(Type)`. `ManagedFieldDescriptor`: `string StorageName`,
  `Func<object?> ValueProvider`, `Func<Type,bool> AppliesTo`, `bool AutoReadFilter` (true = equality axis = a key
  segment; false = non-equality = NOT a key segment). `Koan.Tenancy` already registers `__koan_tenant`.
- **`IStorageGuard`** (`src/Koan.Data.Core/Pipeline/IStorageGuard.cs`): `void Guard(Type entityType)` — throws to
  block (fail-closed). `TenantStorageGuard` already implements it (HostScoped-exempt, posture-aware).
- **`AmbientCarrierRegistry`** (`src/Koan.Data.Core/Ambient/AmbientCarrierRegistry.cs`): `IReadOnlyDictionary<string,
  string>? Capture()` returns the type-less ambient axis bag (axisKey→value) or null — the source for the fail-safe
  path (a raw `IStorageService` call with no entity type).
- **`IdentifierComposer`** (`src/Koan.Core/Naming/IdentifierComposer.cs`): `string Compose(string anchor,
  ReadOnlySpan<Particle> particles, in CompositionPolicy policy)`. `Particle(int order, string axis, string? value,
  ParticlePosition position, string? separator)`; `ParticlePosition.Leading` = `value{sep}anchor`.
  `CompositionPolicy(string separator, IParticleFormatter formatter, int? maxBytes=null)`. `IParticleFormatter`
  = `string? Format(string? value)`.
- **`IStorageService`** (`src/Koan.Storage/Abstractions/IStorageService.cs`): `Put/Read/ReadRange/Delete/Exists/
  Head/TransferToProfile/PresignRead/PresignWrite/ListObjects` — all keyed by `(profile, container, key)` (List by
  `prefix`, Transfer by `key` + target). Registered `AddSingleton<IStorageService, StorageService>()` in
  `src/Koan.Storage/Extensions/StorageServiceCollectionExtensions.cs:19` AND
  `src/Koan.Storage/Initialization/KoanAutoRegistrar.cs:30`.

**Hard invariants (violating any is a bug — see ADR §1–§5):**
1. **Off = byte-identical.** `ManagedFieldRegistry.IsEmpty` AND no `IStorageGuard` registered ⇒ the decorator is a
   pure pass-through (no prefix, no guard call that throws). Apps without `Koan.Tenancy` see ZERO change.
2. **Chokepoint = the decorator at `IStorageService`.** Never put isolation logic only in `StorageEntity<T>`.
3. **Logical vs physical.** `IStorageService`/`StorageObject` carry the PHYSICAL (composed) key; `StorageEntity<T>.Key`
   and all user-visible keys (URLs, presign) carry the LOGICAL key. The decorator composes logical→physical IN.
   `StorageEntity<T>` write-returns set `.Key` from the caller's LOGICAL name, never from `StorageObject.Key`.
4. **Equality only; non-equality fails closed.** Only `AutoReadFilter==true` axes become a particle; a value-yielding
   `AutoReadFilter==false` axis throws on the blob path.
5. **Mandatory sanitizing formatter.** Reject a particle value with `/`, `\`, `..`, a leading dot, or control chars.
6. **Layering.** Do not reference `Koan.Tenancy` from `src/Koan.Storage` (or any `src/` non-tenancy module). Storage
   never names "tenant".

When done: `dotnet build` clean, run the specified tests green, `git add <your files> && git commit` on `dev` (no
push) with the task's message. Report what changed + test counts.

---

## TASK 1 (capable model) — the scope ambient, the sanitizing formatter, and the `ScopedStorageService` decorator

**Goal:** install the chokepoint. After this task, apps WITHOUT tenancy are byte-identical (decorator passes through);
the isolation only activates once an axis is registered (proven in TASK 3).

**1a. Create `src/Koan.Storage/Keys/StorageScope.cs`** (the ambient that lets the type-less decorator learn the
entity type / host-scope intent):

```csharp
using System;
using System.Threading;

namespace Koan.Storage.Keys;

/// <summary>
/// STOR-0011 §2: the ambient that carries the current blob op's entity TYPE (so the type-erased
/// <c>ScopedStorageService</c> decorator can apply <c>ManagedFieldRegistry.ForType</c> + the [HostScoped] exemption
/// + the typed <c>IStorageGuard</c>), or an explicit HOST-SCOPE flag (infra opting out of isolation). When no scope
/// is set, the decorator falls back to the type-less ambient axis bag (fail-safe: isolate by default).
/// </summary>
public static class StorageScope
{
    private static readonly AsyncLocal<Frame?> _current = new();

    internal static Type? CurrentType => _current.Value?.EntityType;
    internal static bool IsHostScope => _current.Value?.HostScope ?? false;

    /// <summary>Enter a typed storage scope for the lifetime of the op (the type-aware layers set this).</summary>
    public static IDisposable For(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Push(new Frame(entityType, false));
    }

    /// <summary>Enter an explicit host scope — the op is unprefixed and unguarded (the IAmbientExempt analog).</summary>
    public static IDisposable HostScoped() => Push(new Frame(null, true));

    private static IDisposable Push(Frame f)
    {
        var prev = _current.Value;
        _current.Value = f;
        return new Pop(prev);
    }

    private sealed record Frame(Type? EntityType, bool HostScope);

    private sealed class Pop : IDisposable
    {
        private readonly Frame? _prev;
        private bool _done;
        public Pop(Frame? prev) => _prev = prev;
        public void Dispose() { if (_done) return; _done = true; _current.Value = _prev; }
    }
}
```

**1b. Create `src/Koan.Storage/Keys/StorageKeyParticleFormatter.cs`** (ADR §4):

```csharp
using System;
using Koan.Core.Naming;

namespace Koan.Storage.Keys;

/// <summary>STOR-0011 §4: rejects a path-unsafe axis value so a particle cannot escape its own prefix.</summary>
public sealed class StorageKeyParticleFormatter : IParticleFormatter
{
    public static readonly StorageKeyParticleFormatter Instance = new();
    private StorageKeyParticleFormatter() { }

    public string? Format(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;        // omit → no particle
        if (value[0] == '.' || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.Contains(".."))
            throw new InvalidOperationException(
                $"Storage axis value '{value}' is not a safe path segment (leading dot, '/','\\\\', or '..').");
        foreach (var c in value) if (char.IsControl(c))
            throw new InvalidOperationException($"Storage axis value '{value}' contains a control character.");
        return value;
    }
}
```

**1c. Create `src/Koan.Storage/Keys/StorageKeyScoper.cs`** — the shared compose+guard the decorator calls. It
resolves the scope from `StorageScope` (typed → `ForType` + typed guard + [HostScoped] exemption) or falls back to
the type-less ambient bag + a value-based guard:

```csharp
using System;
using System.Collections.Generic;
using Koan.Core.Hosting.App;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Storage.Keys;

/// <summary>STOR-0011 §2–§4: guard + compose a logical key into a physical (axis-prefixed) key for a blob op.</summary>
public static class StorageKeyScoper
{
    private const string Sep = "/";
    private static readonly CompositionPolicy Policy = new(Sep, StorageKeyParticleFormatter.Instance);

    /// <summary>Returns the physical key. Throws (fail-closed) if a guard blocks the op.</summary>
    public static string Scope(string logicalKey)
    {
        ArgumentNullException.ThrowIfNull(logicalKey);
        if (StorageScope.IsHostScope) return logicalKey;                 // explicit host scope ⇒ unprefixed, unguarded

        var type = StorageScope.CurrentType;
        if (type is not null) return ScopeTyped(type, logicalKey);       // type-aware path (data-path parity)
        return ScopeAmbient(logicalKey);                                 // fail-safe: raw IStorageService caller
    }

    private static string ScopeTyped(Type type, string logicalKey)
    {
        RunGuards(type);                                                  // typed guard ([HostScoped] exemption inside)
        if (ManagedFieldRegistry.IsEmpty) return logicalKey;
        var managed = ManagedFieldRegistry.ForType(type);
        List<Particle>? ps = null;
        for (var i = 0; i < managed.Count; i++)
        {
            var d = managed[i];
            var v = d.ValueProvider();
            if (!d.AutoReadFilter)                                        // §3 non-equality fails closed if it has a value
            {
                if (v is not null) throw new InvalidOperationException(
                    $"Non-equality axis '{d.StorageName}' cannot scope a blob key (storage is equality-only, STOR-0011 §3).");
                continue;
            }
            var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(s)) continue;                       // guard is the no-scope authority
            (ps ??= new List<Particle>(managed.Count)).Add(new Particle(i, d.StorageName, s, ParticlePosition.Leading, Sep));
        }
        return ps is null ? logicalKey : IdentifierComposer.Compose(logicalKey, ps.ToArray(), Policy);
    }

    private static string ScopeAmbient(string logicalKey)
    {
        var sp = AppHost.Current;
        // Run any registered guards with a generic marker so an active-tenancy host fails closed on an unscoped raw op.
        // (The guard's [HostScoped] check keys on the type; a raw op has none, so it is treated as scope-required.)
        if (sp is not null) foreach (var g in sp.GetServices<IStorageGuard>()) g.Guard(typeof(object));
        var bag = sp?.GetService<Koan.Data.Core.AmbientCarrierRegistry>()?.Capture();
        if (bag is null || bag.Count == 0) return logicalKey;            // off ⇒ byte-identical
        var ps = new List<Particle>(bag.Count);
        var i = 0;
        foreach (var kv in bag)
        {
            var s = StorageKeyParticleFormatter.Instance.Format(kv.Value);
            if (!string.IsNullOrEmpty(s)) ps.Add(new Particle(i++, kv.Key, s, ParticlePosition.Leading, Sep));
        }
        return ps.Count == 0 ? logicalKey : IdentifierComposer.Compose(logicalKey, ps.ToArray(), Policy);
    }

    private static void RunGuards(Type type)
    {
        var sp = AppHost.Current;
        if (sp is null) return;
        foreach (var g in sp.GetServices<IStorageGuard>()) g.Guard(type);
    }
}
```

> **VERIFY before coding the decorator:** confirm `AmbientCarrierRegistry` is registered in DI and reachable as
> `Koan.Data.Core.AmbientCarrierRegistry` (grep `AddSingleton<AmbientCarrierRegistry` / `services.AddSingleton(typeof(AmbientCarrierRegistry`).
> Confirm `TenantStorageGuard.Guard(typeof(object))` does NOT spuriously throw when tenancy is INACTIVE (no tenant
> module / dev posture) — read `src/Koan.Tenancy/TenantStorageGuard.cs`. If `Guard(typeof(object))` mis-fires for the
> no-tenancy case, gate the ambient-guard call on `!ManagedFieldRegistry.IsEmpty`. Report what you found.

**1d. Create `src/Koan.Storage/ScopedStorageService.cs`** — the decorator implementing `IStorageService`, wrapping
an inner instance, calling `StorageKeyScoper.Scope(key)` on the key argument of EVERY method before delegating.
Representative methods (apply the same pattern to ALL 11):

```csharp
using Koan.Storage.Abstractions;
using Koan.Storage.Keys;
// namespace Koan.Storage; sealed class ScopedStorageService(IStorageService inner) : IStorageService

public Task<StorageObject> Put(string profile, string container, string key, Stream content, string? ct_, CancellationToken ct = default)
    => inner.Put(profile, container, StorageKeyScoper.Scope(key), content, ct_, ct);

public Task<Stream> Read(string profile, string container, string key, CancellationToken ct = default)
    => inner.Read(profile, container, StorageKeyScoper.Scope(key), ct);

// TransferToProfile: scope the key ONCE (same tenant, different profile = a tiering copy). Cross-tenant transfer is
// forbidden by the guard and not expressible (ADR MEDIUM/transfer).
public Task<StorageObject> TransferToProfile(string sp, string sc, string key, string tp, string? tc, bool del, CancellationToken ct = default)
    => inner.TransferToProfile(sp, sc, StorageKeyScoper.Scope(key), tp, tc, del, ct);

// ListObjects: scope the PREFIX so a tenant lists only its own blobs.
public IAsyncEnumerable<StorageObjectInfo> ListObjects(string profile, string container, string? prefix = null, CancellationToken ct = default)
    => inner.ListObjects(profile, container, prefix is null ? StorageKeyScoper.Scope("") : StorageKeyScoper.Scope(prefix), ct);
```
Apply to `ReadRange`, `Delete`, `Exists`, `Head`, `PresignRead`, `PresignWrite` identically (scope the `key`). For
`ListObjects` with a null prefix, scope the empty string to get the bare tenant prefix (verify the composer yields
`"acme/"`-style; if `Scope("")` returns `"acme/"` good; if it returns `"acme"` append the separator — test it).

**1e. Register the decorator** at BOTH sites
(`src/Koan.Storage/Extensions/StorageServiceCollectionExtensions.cs:19` and
`src/Koan.Storage/Initialization/KoanAutoRegistrar.cs:30`). Replace `AddSingleton<IStorageService, StorageService>()`
with a decoration: register the concrete `StorageService` and expose `IStorageService` as the decorator wrapping it:
```csharp
services.AddSingleton<StorageService>();
services.AddSingleton<IStorageService>(sp => new ScopedStorageService(sp.GetRequiredService<StorageService>()));
```
(Ensure `StorageService`'s own ctor deps still resolve. If both sites run, make them idempotent — use `TryAddSingleton`
or ensure only one path registers; read both files and keep one authoritative registration.)

**Verify:** `dotnet build` clean. The existing storage suites MUST stay green unchanged (off = byte-identical — no
tenancy referenced there, so `StorageScope.CurrentType` is null, `ManagedFieldRegistry.IsEmpty` is true, and
`Capture()` is null ⇒ pass-through):
```
dotnet test tests/Suites/Storage/Koan.Storage.Core.Tests/Koan.Storage.Core.Tests.csproj --nologo -v q
dotnet test tests/Suites/Storage/Koan.Storage.Connector.Local.Tests/Koan.Storage.Connector.Local.Tests.csproj --nologo -v q
dotnet test tests/Suites/Media/Koan.Media.Core.Tests/Koan.Media.Core.Tests.csproj --nologo -v q
```
**Commit:** `feat(STOR-0011): ScopedStorageService decorator + StorageScope ambient + sanitizing formatter (chokepoint)`.

---

## TASK 2 (capable model) — wire the type-aware layers + the logical-key invariant + infra exemptions

**Goal:** make the decorator learn the entity type (so [HostScoped] + `ForType` work), keep `StorageEntity<T>.Key`
logical, and let infra opt out.

**2a. `src/Koan.Storage/Model/StorageEntity.cs`** — wrap every op that calls `Storage().<op>(...)` in
`using (Koan.Storage.Keys.StorageScope.For(typeof(TEntity))) { ... }` so the decorator sees the type. Apply to ALL:
`CreateTextFile`, `Create<TDoc>`, `Create(bytes)`, `Onboard`, `ReadAllText`, `ReadAllBytes`, `ReadRangeAsString`,
`OpenRead()`, `OpenReadRange()`, `Head()`, `Delete()`, the static `OpenRead(key)`/`OpenReadRange(key)`/`Head(key)`,
`CopyTo`/`MoveTo`. **Logical-key fix:** change `From(StorageObject obj)` → `From(StorageObject obj, string? logicalKey = null)`
and set `se.Key = logicalKey ?? obj.Key;`. Every WRITE op passes the caller's logical `name`: `return From(obj, name);`.
Leave the `Head(key)` fallback `Query(e => ((IStorageObject)e).Key == key, ...)` keyed on the LOGICAL `key`. Leave
`Get(string key, ...)` (sets logical `.Key`) unchanged.

**2b. `src/Koan.Media.Abstractions/Model/MediaEntity.cs`** — the `new`-shadowing static `OpenRead(string key)` (and
any other `new`/shadowing static, e.g. via `MediaEntityExtensions`): either DELETE the override so it inherits the
base (preferred), or wrap its `svc.Read(...)` in `using StorageScope.For(typeof(TEntity))`. Audit
`src/Koan.Media.Core/Extensions/MediaEntityExtensions.cs` for `Url`/presign/`Store` paths that call `IStorageService`
directly and wrap them in `StorageScope.For(typeof(TEntity))` too. Report every site you touched.

**2c. Infra exemptions.** Grep `grep -rn "IStorageService" src/Koan.Data.Backup src/Koan.Web.Backup --include=*.cs`.
The backup services consume `IStorageService` raw with a host `backups` container — wrap their blob ops in
`using Koan.Storage.Keys.StorageScope.HostScoped()` (backups are cross-tenant infrastructure — ADR §2). Report each.

**Verify:** `dotnet build` clean; the same three suites from TASK 1 green. **Commit:**
`feat(STOR-0011): wire StorageScope into StorageEntity/MediaEntity + logical-key invariant + backup host-scope`.

---

## TASK 3 (mid model) — the isolation proof across every surface

**Goal:** prove isolation through a real `AddKoan()` boot (ARCH-0079) with `Koan.Tenancy` + the **Local** provider,
on ALL surfaces the decorator covers — not just `StorageEntity` ops.

**3a.** Add to `tests/Suites/Tenancy/Koan.Tenancy.Tests/Koan.Tenancy.Tests.csproj`:
```xml
<ProjectReference Include="..\..\..\..\src\Connectors\Storage\Local\Koan.Storage.Connector.Local.csproj" />
```
(Add `Koan.Media.Core`/`Koan.Media.Abstractions` references too if you exercise `MediaEntity`.)

**3b.** Extend `Support/TenancyRuntimeFixture.cs` to register a Local storage profile rooted under `root` (mirror the
config in `tests/Suites/Storage/Koan.Storage.Connector.Local.Tests/` exactly — read it for the keys).

**3c.** Add `tests/Suites/Tenancy/Koan.Tenancy.Tests/StorageTenantIsolationSpec.cs` (namespace `Koan.Tenancy.Tests`;
suite is sequential). Cover, each via a real boot under `Posture("Closed")`:
1. `StorageEntity<T>`: two tenants write `"photo.jpg"` → each reads its own bytes (use `Tenant.Use("acme"|"globex")`
   + `TenantBlob.Onboard` + `TenantBlob.Get("photo.jpg").ReadAllText()`).
2. `MediaEntity<T>`: same proof through a `MediaEntity` subclass (its `OpenRead` path) — proves TASK 2b.
3. **Raw `IStorageService`**: resolve `IStorageService` from DI, under `Tenant.Use("acme")` `Put`+`Read` `"r.bin"`,
   under `globex` the same key reads globex's bytes (proves the fail-safe ambient path).
4. **Unscoped** write under `Tenant.None()` (Closed) → throws.
5. `[HostScoped]` blob: written with no tenant, readable under every tenant (unprefixed).
6. **Hostile value**: a fake equality axis whose `ValueProvider` returns `"../globex"` → the op throws (formatter).
7. **Off**: with no tenant scope changes and the existing suites — confirm byte-identical (the existing storage suites
   stay green from TASK 1; nothing to add).
Model the entities + `Posture`/`Isolate` helpers on the existing `AssertNoTenantLeakSpec.cs`. **Prove honesty (RED):**
temporarily disable the decorator registration (TASK 1e), confirm test 1 FAILS (both reads return the last write),
restore, and report you did this.

**Verify:** `dotnet test tests/Suites/Tenancy/Koan.Tenancy.Tests/Koan.Tenancy.Tests.csproj --nologo -v q` all green.
**Commit:** `test(STOR-0011): real-boot storage isolation across StorageEntity/MediaEntity/raw/hostile-value`.

---

## TASK 4 (mid model) — dogfood on S6.SnapVault

**Goal:** make SnapVault multi-tenant-isolated on the blob path (`PhotoAsset : MediaEntity : StorageEntity` inherits
the chokepoint via the decorator) + an acceptance test. SnapVault serves photos through `MediaController` — the very
surface the decorator now covers, so this is the load-bearing dogfood.

**4a.** Add `<ProjectReference Include="..\..\src\Koan.Tenancy\Koan.Tenancy.csproj" />` to
`samples/S6.SnapVault/S6.SnapVault.csproj`. Wire per-request tenant resolution from an `X-Studio-Id` header (read
`src/Koan.Tenancy/` for the resolver seam — grep `ITenantResolver` — and the documented way a host supplies the
tenant; add the minimal middleware/registration in `Program.cs` with a STOR-0011 comment). Do NOT hand-roll
`Tenant.Use` per controller.

**4b.** Create `tests/Suites/Samples/S6.SnapVault.AcceptanceTests/` (mirror an existing sample test csproj, or
`Koan.Tenancy.Tests.csproj`; ProjectReference `..\..\..\..\samples\S6.SnapVault\S6.SnapVault.csproj` + the Local
connector). Boot a real host with the Local provider (temp dir) + `Koan.Tenancy` + a no-Docker data adapter
(`Koan:Data:Sources:Default:Adapter=sqlite`) + AI disabled. Assert isolation on SnapVault's **real `PhotoAsset`**
storage path: studio-a and studio-b each store a `"sunset.jpg"` blob → each reads its own; an unscoped store fails
closed. **If full `PhotoAsset` boot is impractical** (the `[Embedding]`/`MediaEntity` machinery needs AI/Mongo),
fall back to a minimal `S6.SnapVault`-namespace `MediaEntity<DogfoodPhoto>` proving the same, plus an assertion that
`typeof(PhotoAsset).IsSubclassOf(typeof(Koan.Storage.Model.StorageEntity<PhotoAsset>))`. Prefer the real path; report
which you used and why. Add the test project to `Koan.sln` if test projects are listed there (`grep S6.SnapVault Koan.sln`).

**Verify:** the acceptance test green + `dotnet build samples/S6.SnapVault/S6.SnapVault.csproj` clean. **Commit:**
`feat(STOR-0011): dogfood storage tenant isolation in S6.SnapVault + acceptance test`.

---

## TASK 5 (orchestrator) — regression, review, ledger

1. Green battery: the three storage/media suites + the full tenancy suite + the data-core off-proof
   (`tests/Suites/Data/Core/Koan.Tests.Data.Core` — 271).
2. Run an impl-diff adversarial review of TASK 1+2 (Workflow, refute-by-default lenses: leak-surface / logical-key /
   off-byte-identical / composition / the `ScopeAmbient` guard-misfire) and fold confirmed findings.
3. Mark STOR-0011 `Accepted`; flip the GAP C 0.4 line in `docs/architecture/redesign-completion-ledger.md` to ✅ with
   commit hashes + green counts. Commit `docs(STOR-0011): mark storage blob-key axis isolation done (gap C 0.4)`.

**Then:** gap C **0.3 vector** (Weaviate row-discriminator, axis-generic with ARCH-0098) — OUT OF SCOPE here (needs Docker).
