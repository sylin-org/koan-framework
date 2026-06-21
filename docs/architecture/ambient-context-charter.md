---
type: ARCHITECTURE
domain: core
title: "Ambient Context — Design Charter & Truth-Test (Facet 3)"
audience: [architects, developers, ai-agents]
status: draft
last_updated: 2026-06-21
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-21
  status: verified
  scope: docs/architecture/ambient-context-charter.md
---

# Ambient Context — Design Charter & Truth-Test (Facet 3)

> **What this is.** Not a design. The **truth-test** a design must pass. It coalesces a full
> investigation — an empirical survey of every ambient mechanism in Koan, an archaeology of *why*
> each was built that way (genuine reasons vs cargo-cult), and external research into how the rest of
> the world (Go, OpenTelemetry, Java ScopedValue, Kotlin, React, Rails, ABP, EF Core, Finbuckle) has
> learned to do ambient/scoped/tenant context — into the laws and acceptance criteria any new Ambient
> surface will be measured against. Builds on, and supersedes the sketch in,
> [foundation-consolidation-plan.md §4.3](./foundation-consolidation-plan.md).

The two mandates, which every line below serves:

- **Simplification** — *"fewer but more meaningful moving parts."* Today there are **7+ ambient
  mechanisms across 5 pillars, implemented 5 different ways.** The target is **one.**
- **Delight** — *"what would developers love to have?"* Measured against the north stars in §3.

Greenfield, break-and-rebuild is **desired**. This document is allowed to invalidate existing surfaces.

---

## 1. The problem today (survey)

Seven ambient mechanisms, five implementations, no shared primitive. They overlap, interact, and
disagree on every design axis.

| Mechanism | Pillar | Backing store | Scope model | Carries |
|---|---|---|---|---|
| **`EntityContext`** | Data | `AsyncLocal<record>` | push/pop, **restore-to-prior**, inherit-unless-overridden | source · adapter · partition · transaction(+coordinator) · cacheBehavior |
| `AppHost.Current` | Core | **hybrid** `static _global` + `AsyncLocal _scoped` | split: setter = overwrite-no-restore / `PushScope` = push-pop | root `IServiceProvider` |
| `KoanEnv` | Core | **frozen** `static` snapshot | init-once, never refresh | env/identity (**config, not ambient**) |
| `_bootFailures` | Core | **`ThreadStatic`** | mutable accumulator | boot failures |
| `CacheScopeAccessor` | Cache | `AsyncLocal<`**`Stack`**`>` | mutable-stack push/pop | cache key region/scope — **dormant** |
| `Client._override` + `AiCategoryScope` | AI | `AsyncLocal<single>` + `AsyncLocal<`**`Stack`**`>` | overwrite-restore + stack | pipeline override (**test-only**) · per-category source/model |
| `McpCallContext` | MCP | **DI-Scoped mutable holder** | overwrite-in-place, set-once-per-scope | calling `ClaimsPrincipal` |

**Two categories, not one** — conflating them is the trap `AppHost` already fell into:

- **Flowing scoped context** (per-operation, `AsyncLocal`): `EntityContext`, AI scopes, `CacheScope`,
  MCP principal → *the unification target.*
- **Frozen infra** (process/host lifetime): `KoanEnv` snapshot, `AppHost._global` provider, identity,
  boot `ThreadStatic` → *a different tier; the primitive layers over it, does not absorb it.*

**`EntityContext` is the exemplar** — immutable record, derive-on-`With`, restore-on-dispose nesting,
inherit-unless-overridden. Everything else is a weaker, divergent reinvention.

**Concrete defects the fragmentation already caused** (not hypotheticals): `CacheScope`'s mutable
`Stack` is racy under fan-out; `AppHost._global` leaks a disposed host across multi-host tests; the AI
`EmbeddingWorker` set-site is a no-op when only `Model` is set; `McpCallContext.Principal` has a public
setter (clobberable mid-flight); `_bootFailures` drops failures after a thread hop.

**The missing dimension — tenancy.** There is *no* tenant concept. `partition` does dataset routing
with no identity/isolation/propagation, and the mutable process-wide statics actively defeat in-process
tenant isolation. This is the hard gate on multi-tenancy (P4.1).

---

## 2. What the history says — keep vs discard (archaeology)

Per-mechanism verdicts from git + ADR archaeology. The point: a greenfield must keep the **genuine
reasons** and delete the **accretion, cargo-cult, and dead surface**.

### `EntityContext` — mostly right, partly accreted, with stale-ADR lies
- **Keep:** `AsyncLocal` + using-scope + **restore-previous-on-dispose** (right since day one,
  `16fe1a96`); the immutable record with `with`-derivation; **inherit-unless-overridden** (load-bearing
  — without it, opening a partition scope would drop the ambient transaction); Source XOR Adapter
  exclusivity; partition front-door validation (a real data-bleed guard); rollback-on-dispose default;
  the ARCH-0084 split of capability tokens from runtime state; deferred-tx as an **opt-in layer beside**
  the routing dimensions, not baked into every `Save` signature.
- **Discard / fix:** `cacheBehavior` rides on the routing record but is **semantically orthogonal** (a
  read-side hint, not a router) — accretion; the **`AppHost.Current` service-location inside `With()`**
  is a *missing-primitive workaround* (a static can't be DI-injected); stale-ADR **lies** to retire —
  DATA-0077's "Nested `With()` calls **REPLACE, not merge**" invariant (the code does merge), its old
  partition regex, and DATA-0078's "**auto-commit / zero cognitive load**" headline (the code now
  defaults to rollback). *Lesson: the exemplar's own ADRs document behavior it abandoned.*

### `AppHost` / `KoanEnv` — workarounds and a grab-bag
- **Keep:** `KoanEnv`'s frozen init-once snapshot (answers before DI exists, kills env-check drift —
  **but it is config, not ambient: do not fold it**); the flow-scoped `AsyncLocal` override; the single
  ambient *seam* that makes `Entity<T>.Get(id)` "just work" (load-bearing ergonomics); fail-loud boot;
  **first-host-wins as a concept**.
- **Discard / fix:** the **split-brain** (`_global` static + `_scoped` AsyncLocal) is a workaround for a
  missing primitive (DATA-0095 wanted one clean `AsyncLocal`); first-host-wins lives in **one** binder
  call-site, silently *not* enforced at the other ~4 setters → move it **into the primitive**; the boot
  **`[ThreadStatic]` is cargo-cult** (boot is single-threaded; the attribute is inert); `AppHost`
  conflating ambient-SP + app-identity + scope is **grab-bag accretion** (identity rode in "because
  `AppHost` was there").

### Cache — one dead, one good, falsely framed as "two concerns"
- **Keep:** the `EntityContext`-model ambient (immutable record, inherit-merge, restore); **read-side
  only + writes-always-invalidate** (ARCH-0075 #8 — the correctness guarantee); the 3 honest
  `CacheBehavior` modes mapped in one place; **deriving the cache-key partition from the *same* ambient
  that routes data** (proof that one ambient can serve routing *and* cache).
- **Discard:** **`CacheScope*` is dormant — zero production callers**; its **mutable `Stack` is an
  accidental divergence** that predates the immutable pattern; the "`Push onto AsyncLocal STACK`" doc
  comment is misleading (it's a single record); the "two ambients = two clean concerns" framing is
  **accretion, not design** (the consolidation plan already classifies both as redundant-to-merge).

### AI — valuable core, dead seams, three idioms where one would do
- **Keep:** **per-category composable routing** (outer sets `chat=`, inner sets `embed=`, both apply —
  genuinely valuable); `AsyncLocal` + immutable; innermost-wins with explicit-call-site override on top;
  reference-equality dispose guard; the *conceptual* split of "swap the whole engine (test/host)" vs
  "route this category"; independent slots for task-verbs so scoping `Chat` doesn't silently re-route
  the verbs that call it.
- **Discard:** the per-category **model** plumbing is **unreachable through the public API** (half-built);
  **`Client._override` is a test-only seam, zero production callers**; the 15-way hard-coded per-category
  fan-out is pure accretion; raw-string verb keys vs `AiCapability` constants invite typo bugs; and there
  are **three hand-rolled `IDisposable`-scope idioms** (Client, AiCategoryScope, EntityContext) where one
  shared primitive belongs.

### MCP — a deliberate security stance to *respect*, not absorb
- **Keep (as a constraint on the design):** MCP and web **deliberately thread `ClaimsPrincipal`
  explicitly** at every hop — a visible, typed, reviewable argument — and **fail closed** (null principal
  → anonymous → denied at the data layer). This is a security choice (SEC-0004 §3.3), not an
  inconsistency. A unified Ambient must **not** quietly turn principal into invisible ambient state; if it
  carries principal at all, it is an explicit, opt-in, fail-closed slice that preserves reviewability.

---

## 3. What the world says — the convergent pattern (external voices)

Independently, every mature system converged on the **same** model. (Sources at the end.)

**The universal pattern** — *immutable, one carrier of typed slices, derive-on-write, nearest-wins
shadowing with auto-revert, block-scoped, one-way, bound to the unit of work:*

1. **Immutable, derive-on-write.** Every change returns a *new* context; the parent never mutates.
   (Go `context`, OTel Context/Baggage, Java `ScopedValue`, Kotlin `coroutineContext`.) The fan-out race
   is *entirely* a consequence of sharing a mutable reference — make a mutable bag **unrepresentable**.
2. **One carrier, many *typed* slices** — not N stringly-keyed ambient globals. Kotlin `get(Key<E>): E?`
   is the gold standard. The untyped grab-bag (`ctx.Value any`, Rails `Current` junk-drawer) is
   universally regretted; string keys are a latent cross-package clobber.
3. **Nearest-wins shadowing + auto-revert on scope exit.** Nesting shadows the outer binding for the
   subtree; leaving restores the parent. Never mutate an outer binding from inner. (React providers,
   Kotlin `+`, Java nested `where()`, Go derived chain, **ABP `CurrentTenant.Change()`**, EntityContext.)
4. **Block-scoped lifetime you can *see*.** `using(scope)` / `with(KEY,v).run{}`. "After the block, it's
   gone." This single property kills both the leak *and* the "where is this set?" problem.
5. **One-way, read-only to consumers, bound to the work** (`AsyncLocal`/flow, never `ThreadLocal`). A
   value set after an `await` does **not** flow up — design *for* that, don't be surprised by it. Any
   boundary where context evaporates — `await` continuation, **background job, message queue** — is a
   **P0 bug** the framework must auto-flow across.

**Tenancy laws** (the multi-tenancy unblock):

- **Fail-CLOSED.** No tenant set → a tenant-scoped read/write **throws** (Rails `require_tenant`), never
  "return everything." Make "no tenant" a loud error in dev/test, not a silent prod leak.
- **One explicit, scoped, exception-safe switch** for cross-tenant / host / background work —
  **`using (Tenant.Change(id))`** (ABP's standout primitive, which is *literally* `EntityContext.With`
  applied to tenancy). Ambient-by-default + explicit-switch-for-crossing.
- **Auto-stamp tenant on WRITE**, not only filter on READ. **Null tenant = Host/shared** (clean modeling
  of platform rows alongside tenant rows under one filter).
- **Push the predicate to the lowest layer.** Koan owns `Entity<T>`'s read path → inject the tenant
  filter there *once* → a forgotten `WHERE` becomes **structurally impossible**.
- **Multi-AXIS.** Tenant must auto-flow into *every* framework-owned shared resource — **cache keys,
  cache-coherence channels, observability tags, connection-pool session vars, message headers.** "The
  leak is usually an orthogonal resource, not the DB."
- **Escape hatches** (cross-tenant / ignore-filter / raw) must be **explicit, narrow, greppable**, and
  must **not** widen isolation as a side effect of an unrelated concern (don't let "ignore soft-delete"
  also drop the tenant filter — separate, named filters).
- **Lifetime/caching:** anything caching per-tenant config (connection strings, compiled options) must be
  keyed on / invalidated by the tenant; mind Scoped-vs-Singleton DI lifetimes.

**Delight north stars** (the "Delight" axis of the truth-test):

> *It just flows and I never think about it · I can **see** the lifetime in the code · immutable + typed ·
> fail-fast with a **name-the-fix** error · **one** obvious way (not three) · secure-by-default
> (can't accidentally touch another tenant) · survives async/jobs · hide the raw primitive behind a
> curated typed front door · the API is honest about what it requires.*

The developer **hate-list** is a mirror of Koan's current state: *"two-or-more ways to do the same
thing"* = cache-behavior-in-3-places; *"where is this set?"* = 7 scattered ambients; *nullable-everywhere
reads*; *context lost across async/jobs*; *hidden magic dependencies*. Fixing the fragmentation **is** the
delight.

---

## 4. The Laws (invariants any design MUST obey)

Synthesized from §2 (keep) + §3 (voices). A proposed surface that violates a law is rejected.

- **L1 — One carrier.** Exactly **one** flowing ambient primitive (working name `Ambient`). It collapses
  `EntityContext`, the AI scopes, and the cache behavior onto itself. *No second ambient mechanism may
  be added.*
- **L2 — Immutable, derive-on-write.** The context is an immutable value; a scope produces a *new* value.
  A mutable ambient container is unrepresentable in the API. (Kills the fan-out race; kills clobbering.)
- **L3 — Typed slices.** Composable, **strongly-typed** slices (`Ambient.Current.Get<TSlice>()` /
  first-class accessors), never stringly-keyed bags. Each pillar contributes a slice without touching the
  others.
- **L4 — Scoped lifetime, restore-not-null.** One `using`-scope verb. Nesting **shadows**; dispose
  **restores the parent** (never nulls). Lifetime is visible in code structure.
- **L5 — Flow-safe by construction.** `AsyncLocal`-backed; flows across `await`. The framework
  **auto-propagates** the relevant slices across the boundaries that lose them today — **background jobs
  and messaging** (capture into the payload, restore at handler entry) — and offers a **sanctioned
  suppress-flow** for detached work.
- **L6 — Fail-closed + fail-loud.** A *required* slice that is unset (e.g. tenant in a tenant-scoped op)
  **throws** with a message that **names the fix** ("no tenant in scope; wrap in `using
  (Tenant.Change(id))` or set tenant resolution"). Reads of required slices are **not nullable** at the
  call site.
- **L7 — One way + one escape hatch.** Zero-ceremony default (no param threading, no attributes); exactly
  **one** explicit override per concern (`Ambient.Push(...)`, `Tenant.Change(...)`). No redundant second
  mechanism for the same concern.
- **L8 — Leaks structurally impossible.** Isolation (tenant, partition) is enforced at the **lowest
  framework-owned layer** (the repository/read path), not by per-query discipline. Escape hatches are
  explicit, narrow, greppable, and never widen isolation as a side effect.
- **L9 — Tenancy is multi-axis and first-class.** A tenant slice flows automatically into data filtering,
  **write-stamping**, cache keys, coherence channels, observability tags, and (where used) pool session
  vars. Null tenant = host/shared.
- **L10 — No service-location, no grab-bag.** The primitive is DI-aware without reaching into a static
  provider mid-operation. Capability/config is separated from runtime state. The frozen infra tier
  (`AppHost` root provider, `KoanEnv`) is **layered over, not absorbed** (different lifetimes).
- **L11 — Honest + introspectable.** The API hints that scoped context is in play (a scope object / a
  named typed accessor), and there is a **`Ambient.Describe()`** debug view of the current scope chain —
  "who set what, where" answerable in seconds.

---

## 5. The Truth-Test (acceptance criteria)

A proposed Ambient surface is run against every question. **All must pass.** Phrased so the answer is
unambiguous.

### Simplification
- **S1.** Is there now **exactly one** flowing ambient mechanism? (Count them. Target: 1.)
- **S2.** Are `CacheScope*`, `Client._override`, and the unreachable AI model-scope **deleted** (not
  migrated)? (Dead surface must die.)
- **S3.** Is cache *behavior* controlled in **one** place (not three: EntityContext + CacheScope + web
  middleware)?
- **S4.** Is `KoanEnv` left as config (not folded), and `AppHost` root provider layered-over (not
  absorbed)? Is the `AppHost` split-brain resolved to one mechanism, with first-host-wins **in the
  primitive**?
- **S5.** Is there exactly **one** `IDisposable`-scope idiom in the framework (not three hand-rolled)?

### Delight
- **D1.** Common path: can a developer read/write entities, with the right tenant/partition/routing, **without threading any parameter or adding any attribute**?
- **D2.** Can a reader **see the lifetime** of a scope in the code (block-scoped `using`), and answer "is this value live here?" without a project-wide search?
- **D3.** Is every slice **strongly typed** at the read site (no stringly keys, no `object`)?
- **D4.** When a required slice is missing, is the error **fail-fast and fix-naming** (not a cryptic null/`NullReferenceException` deep in business logic)?
- **D5.** Is there **one** obvious way to read each slice (a curated typed front door), with the raw primitive hidden?
- **D6.** Can a developer answer "**where was this set?**" in seconds (`Ambient.Describe()`)?

### Correctness / Tenancy
- **C1.** Does the context **flow across `await`** with no ceremony? (table-stakes)
- **C2.** Does the tenant/routing context **flow into a background job and a published message automatically** — captured at enqueue, restored at handler entry — with the developer doing nothing? (The #1 industry failure mode.)
- **C3.** Is mutation impossible — can two parallel `Task`s **not** clobber each other's context? (immutability)
- **C4.** On a pooled thread/connection, does the next operation **never inherit** the previous one's tenant? (no leak-on-reuse)
- **C5.** Does a tenant-scoped read with **no tenant set THROW** (fail-closed), not return all tenants' rows?
- **C6.** Does a **forgotten `WHERE`** leak across tenants? (Must be **NO** — enforced at the repository layer, structurally impossible.)
- **C7.** Does the **cache key include the tenant** automatically, so Tenant A's cached value can't serve Tenant B?
- **C8.** Does a **write** auto-stamp the current tenant, so a row can't be created tenant-less or cross-tenant?
- **C9.** Are cross-tenant / ignore-filter **escape hatches explicit, narrow, greppable**, and do they avoid widening isolation as a side effect of an unrelated filter?
- **C10.** Does setting a value in a child scope correctly **not** leak up to the parent scope? (one-way)

### Constraints respected
- **R1.** Is `ClaimsPrincipal` still **explicit and reviewable** at security boundaries (MCP/web) — i.e. the unified primitive did **not** quietly turn principal into invisible ambient state?
- **R2.** Is the whole thing **NativeAOT-clean** (reflection-free, no `dynamic`, no service-location-mid-op)?
- **R3.** Are the **good guarantees preserved**: writes-always-invalidate-cache; Source XOR Adapter; partition validation; deferred-tx as an opt-in layer; rollback-on-dispose default?

---

## 6. The design forks — resolved by the evidence

The five open tensions from the problem-space, now answered (recommendations; the architect ratifies):

1. **Scope boundary → flowing-only.** The primitive unifies the *flowing scoped* tier and **layers over**
   the frozen infra tier. `KoanEnv` stays config; `AppHost` root provider stays a host singleton (the
   primitive *uses* it, doesn't *become* it). Evidence: the two-categories survey + the archaeology
   ("split-brain is a workaround, not a two-lifetime design") + the consolidation plan's own §128.
2. **Single value vs layered slices → one immutable carrier of typed slices.** Routing is one record;
   cache and AI need per-key layering → model them as typed slices with nearest-wins shadowing. Evidence:
   Kotlin/OTel composability + AI's genuine per-category need.
3. **Principal in or out → out by default, opt-in explicit slice at most.** Respect the deliberate
   explicit-threading security stance (L-constraint from MCP archaeology + SEC-0004). Never invisible.
4. **Migration → additive promotion, then deletion.** Promote `EntityContext`'s pattern to *the*
   primitive; re-home cache/AI onto it; **delete** the dead surfaces (`CacheScope`, `_override`,
   half-built model-scope). Don't break the exemplar's ergonomics.
5. **Tenancy → first-class slice with isolation teeth.** Not "partition renamed." Tenant carries
   identity + fail-closed + multi-axis auto-flow + repository-level enforcement. Partition remains the
   *dataset* axis; tenant is the *isolation* axis. (`"tenant-42"` as a partition value, in the §4.3
   sketch, is exactly the conflation to avoid.)

---

## 7. Explicitly out of scope / NOT this

- **Frozen infra** (`KoanEnv`, `AppHost._global` root provider, app identity) — a separate tier; layered
  over, not absorbed.
- **Cross-process / wire propagation** of ambient values (the OTel/W3C-Baggage problem) — out of scope
  beyond what tenancy + jobs/messaging require; if added later, it is explicit, size-bounded, and never
  carries secrets/PII.
- **A general DI replacement** — this is request/operation-scoped *metadata + routing + isolation*, not a
  parameter-passing channel. A genuine input is still an explicit parameter (Go's own line in the sand).

---

## 8. Open questions for the architect

1. **Naming.** `Ambient` collides conceptually with the existing `Koan.Context` pillar; `EntityContext`
   already exists. Candidates: `Ambient`, `Scope`, `Koan.Current`, `ExecutionContext` (taken by BCL).
2. **Tenant enforcement mechanism.** Repository-level query filter (portable, all adapters) vs
   DB-enforced RLS backstop (Postgres) vs both. The charter requires *structural* enforcement (C6); which
   mechanism(s) per adapter is open.
3. **How far `Ambient` absorbs `AppHost.Current`.** Layer-over is decided (fork 1); the exact seam (does
   `Ambient.Current.Services` delegate to the host provider? does scope-push also push a DI scope?) is
   open.
4. **Tenant resolution surface.** Strategy chain (host/subdomain/route/header/JWT-claim) à la Finbuckle —
   in scope for the primitive, or a thin layer above it?
5. **Principal slice.** Out by default (fork 3) — but is there an opt-in `Ambient` principal slice for
   non-web/non-MCP surfaces (jobs, background) that today have *no* principal at all?

---

## Sources

External research (practitioner voices + prior art): OpenTelemetry
[Context](https://opentelemetry.io/docs/specs/otel/context/) ·
[Baggage](https://opentelemetry.io/docs/concepts/signals/baggage/); Go
[context blog](https://go.dev/blog/context); Java
[Scoped Values (JEP 446)](https://openjdk.org/jeps/446); ABP
[Multi-Tenancy](https://abp.io/docs/latest/framework/architecture/multi-tenancy);
[Finbuckle.MultiTenant](https://www.finbuckle.com/MultiTenant);
[EF Core global query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters);
Rails [CurrentAttributes](https://api.rubyonrails.org/classes/ActiveSupport/CurrentAttributes.html);
[The Ambient Context Anti-Pattern (Manning)](https://freecontent.manning.com/the-ambient-context-anti-pattern/);
[Float the state instead (planetgeek)](https://www.planetgeek.ch/2016/04/26/avoid-threadstatic-threadlocal-and-asynclocal-float-the-state-instead/);
[Implicit Async Context (Stephen Cleary)](https://blog.stephencleary.com/2013/04/implicit-async-context-asynclocal.html).

Internal evidence: the ambient survey, archaeology, and external-voices workflows (2026-06-21);
[foundation-consolidation-plan.md](./foundation-consolidation-plan.md) §4.3; ADRs DATA-0077, DATA-0078,
ARCH-0075, ARCH-0084, SEC-0004; `EntityContext.cs`, `AppHost.cs`, `CacheScopeAccessor.cs`, `Client.cs`,
`AiCategoryScope.cs`, `McpCallContext.cs`.
