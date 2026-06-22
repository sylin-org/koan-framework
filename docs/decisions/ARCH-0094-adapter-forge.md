# ARCH-0094: The Adapter Forge — agent-authored, conformance-gated adapters

**Status**: Accepted (2026-06-21) — *implementation queued behind tenancy ([ARCH-0095](ARCH-0095-tenancy.md)); this ADR records the decision.*
**Date**: 2026-06-21
**Deciders**: Enterprise Architect
**Scope**: How Koan grows its set of provider adapters (data, auth/OAuth, storage, messaging, vector, AI, cache, …) **without being bounded by maintainer time** — by directing an agent to author a conformant adapter for any seam/vendor, gated by an objective behavioral verifier. Defines the capability (**the Adapter Forge**), the per-adapter-type authoring artifact (**the Adapter Blueprint**), and the verifier (**the Conformance Gate**).
**Related**: **ARCH-0084** (unified capability model — the contract adapters announce) · **ARCH-0079** (integration tests as canon — the real-store oracle) · **ARCH-0091** (Testcontainers harness) · **DATA-0104** (the capability-honesty oracle) · the tenancy design ([tenancy-design.md](../architecture/tenancy-design.md)) — Adapter Forge is the structural answer to that effort's unanimous "owns-every-axis lock-in" adoption barrier, and the Conformance Gate **is** the same artifact as the tenancy isolation test-kit (P7). Full design journey: [adapter-forge.md](../architecture/adapter-forge.md) (brief) + the external-review RFC.

---

## Context

Koan's defining strength — it **owns every backend pillar in one runtime** — is also its sharpest
adoption risk. The tenancy external review found, unanimously, that the **fatal barrier** for existing or
enterprise codebases is infrastructure lock-in: a team that wants Koan's tenancy/isolation but is mandated
to run, say, Pinecone vectors or an enterprise Kafka bus Koan doesn't own faces "rip out your approved
stack or walk away." The supported-adapter set was bounded by what the maintainer had time to write.

The reframe: most frameworks that call themselves "agent-native" mean *agents can use the framework.*
Koan's claim is stronger — **the framework extends itself through agents.** The supported-adapter set
becomes bounded not by maintainer time but by **what an agent can generate and verify on demand.** This
requires (a) a way to *guide* an agent to build a good adapter, and (b) an *objective* gate that makes a
generated adapter trustworthy without trusting the generator. Agents write code fluently but skip the
*process* (they don't check what exists, don't research, don't hunt for gotchas, don't test) — so the
guidance must scaffold the missing discipline, and the gate must prove it was followed.

---

## Decision

### 1. The capability — the Adapter Forge

Koan ships a capability by which an agent authors a conformant adapter for an un-owned seam/vendor:
`koan adapter new --seam <pillar> --provider <vendor>` → the agent reads that seam's **Adapter
Blueprint** → produces an adapter → runs the **Conformance Gate** against a real instance → **green =
shippable.** This is the *contribution* half of agent-native (paired with the *consumption* half: the
projection / MCP work). It softens the thesis from "**owns** every axis" to "**coordinates** every axis,
and grows a new one on demand," and loosens the otherwise greenfield-only go-to-market.

### 2. The artifact — the Adapter Blueprint

A per-adapter-type **good-implementation-hygiene script** (one per type across every extensible pillar —
"How to build a Data adapter to a SQL database," "How to implement an OAuth adapter," "…a storage
adapter," "…a messaging-queue adapter," …). Parallel to **cards**: *cards* tell an agent how to *use* a
pillar; *blueprints* tell it how to *extend* one. Each scripts the same hygiene:

> **Discover** (find the blueprint by intent; **check NuGet / the catalogue for an existing adapter
> first** — reuse before build) → **Research** (probe a live instance with a **limited-privilege**
> credential to learn its real capability/transaction/query model; identify the contract + the capability
> tokens to announce) → **Check online for resources / how-tos** → **Implement** (a generic, conformant
> adapter satisfying the obligations) → **Check for gotchas** → **Test** (run the Conformance Gate).

**Three binding principles:**
- **Agent-optimized** — authored *for an agent to execute* (directive, machine-actionable), not human prose.
- **Vendor-agnostic** — one per adapter *type*, **never per vendor**; vendor specifics are **discovered at
  runtime, not encoded**, which *structurally requires* the empirical-probe model and keeps the set small.
- **Grounded in factual first-party code** — distilled from Koan's own shipped, Gate-passing adapters (the
  obligations/patterns/gotchas are the common denominator across the real Postgres/SQL Server/Mongo/…
  implementations; their *differences* are what the agent discovers). Like cards, checkable against the
  source — it can't drift into fiction.

The Blueprint **enforces the hygiene and states the obligations** (isolate at the chokepoint; ACID where
it claims transactions; push down what it announces; carry the ambient context; fail-closed; honor
classification). It **does not prescribe the optimal/performant code** — that is the author's craft.

### 3. The verifier — the Conformance Gate

The objective acceptance gate. **Black-box-first** (verifies what the adapter *does*, not how it's
written), **capability-driven**, **real-store only** (ARCH-0079), and **biased toward strictness** (a
false negative is catastrophic; a false positive merely annoying).

- **Capability-flag-driven + "no capability-lies."** The adapter announces a `CapabilitySet`; the Gate
  runs exactly the conformance modules matching each flag (un-announced ⇒ skipped). A capability token and
  its conformance module are **co-defined** — `Caps.X` cannot exist without `ConformanceModule.X` — so
  **over-claim fails green, structurally.**
- **Behavioral layers:** honesty (does the surface behave as each flag claims?) · surface (every verb?) ·
  correctness (an oracle vs a CLR reference / cross-adapter convergence) · isolation+classification
  (tenant-isolation fuzz across *every* path **including raw/bulk**; carry-tenant; honor `[Phi]`;
  fail-closed; **errors leak no cross-tenant identifiers**).
- **Beyond the happy path (mandatory for high-blast):** **contention** (saturate the pool, N workers × M
  tenants → catch connection-state carryover / session poisoning) · **soak** (N-thousand ops, measure the
  process's handle/connection/memory footprint → resource leaks) · **chaos/fault-injection** (a
  Toxiproxy/Jepsen-style proxy drops/delays/severs calls → prove it fails **closed**, never open) ·
  **durability/restart** (restart mid-run → catch an in-memory shim that returns rows but persists
  nothing). The harness **reuses one adapter instance across tenants** (mimicking the production singleton
  — a fresh-per-test harness cannot catch instance-state leaks).

It is the *same artifact* that (1) keeps the ARCH-0084 capability model honest today, (2) is the tenancy
isolation/classification proof (P7), and (3) gates agent-authored adapters.

### 4. Blast-radius is the data classification carried, not the infrastructure category

An adapter's blast-radius is the **highest data-classification tier permitted to ride its capability
tokens** — *never* its pillar. A vector adapter carrying `[Phi]` (`Embeddable = true`) is **high-blast**
(cross-tenant RAG exfiltration / prompt-injection), not low. Tiering is therefore **dynamic** and ties
the Forge's trust model directly to the classification axis (ARCH-009x tenancy):

| Blast (by data carried) | Gate |
|---|---|
| **High** (PHI/PII/PCI/Secret rides the adapter) | full behavioral suite **+ contention + soak + chaos + durability** · a **narrow static lint** (Roslyn/AST denylist of structurally-dangerous patterns — `static` mutable state, in-memory data shims, unmanaged threads, missing connection-lifecycle hooks, raw-error passthrough; the eBPF-verifier model, **not** a correctness review) · **human diff-review of only the isolation-critical lines** (tenant-predicate injection + connection lifecycle) |
| **Medium** | full behavioral suite + contention |
| **Low** (public data) | the behavioral suite |

### 5. The two boundaries (and where they decouple)

- **We never prescribe the optimal code** (craft is the author's) — this holds at every tier.
- **We are black-box-first**, but for **high-blast** the "never read the code" purity is deliberately
  relaxed to a **narrow forbidden-pattern lint + an isolation-line review** — defense-in-depth, because
  the residual error-path / async-context-race risk is real and no black-box test fully automates it.
  Koan's own `EntityContext` restore-on-scope-exit bounds any such leak to a *single* mis-routed
  operation (never persistent), and the Gate tests exactly that (fault inside a call → assert the *next*
  call's tenant context is correct).

### 6. Trust over time, version-binding, and governance of generated code

- **Maturity lifecycle** (Dapr-style): **conformant** (passes the Gate) → **proven** (N months in
  production, no incident) → **certified** (human review + soak + lint). A signal beyond binary pass/fail.
- **Version-binding + fleet regression** (JDBC/Crater-style): a green result is bound to *(framework
  version, provider version)*; on a change, re-run the Gate across every adapter to detect drift.
- **Generated code is a regenerable build artifact, not hand-maintained.** It lands in the consumer's
  source control (for same-DX), but day-2 maintenance is **re-run the Blueprint → regenerate →
  re-verify**, not patch-by-hand. The Blueprint + Gate are the durable assets; the adapter is their output.

### 7. Empirical capability discovery + reuse-first (two honesty gates, one flywheel)

The agent **probes the live target** (least-privilege) to learn real capabilities and announces *only*
what it confirmed — and the Gate independently verifies what was announced. Over-claim is caught either
way (two stacked honesty gates). The Blueprint's first step is **reuse-before-build** (check NuGet / the
catalogue); authoring is the fallback. This compounds: every Gate-passing adapter an agent publishes
grows the pool, and the **same Gate that proves an agent's adapter is what lets a team trust a community
adapter they didn't write** — run its Gate against their instance; green = trust.

---

## Consequences

- **The lock-in barrier is defused.** "We don't support your infrastructure" becomes a 24-hour
  generate-verify-deploy (the *procurement flip*), and a vendor licensing change (Redis → Garnet) becomes
  a config swap (*substrate hot-swap*). The greenfield-only GTM loosens.
- **One artifact, three payoffs.** The Conformance Gate keeps ARCH-0084 honest, is the tenancy P7 proof,
  and gates the Forge — built once.
- **The trust model is honest, not magical.** Black-box behavioral proof + (for high-blast) a narrow lint
  + an isolation-line review + a maturity ladder — a defensible answer to "an agent wrote the isolation
  code," and *more* reliable than human review for the properties the Gate covers.
- **The blueprint set stays small and durable** (one per type, vendor-agnostic, grounded in real code) —
  consistent with "fewer but more meaningful parts."
- **Sequencing.** This is a **framework-wide capability**, likely its own facet beside the redesign's
  Facet 4. Implementation is **queued behind tenancy**: the tenancy ADR ships first; the Conformance
  Gate's first incarnation is the tenancy isolation test-kit (P7); the Forge generalizes from there.

### Carve-outs / open (for the implementation ADR follow-on)

- The exact Conformance Gate **module set and thresholds** (soak counts, contention concurrency, chaos
  fault rates) and the **static-lint denylist** are implementation detail.
- The **Blueprint catalogue / discoverability** mechanism (how an agent matches "old Oracle" → the right
  blueprint) is to be designed.
- The **human-in-the-loop thresholds** per maturity/blast tier (when sign-off is mandatory) are to be set.
- **Performance is never prescribed** — the Forge produces *generic-but-proven* adapters; optimization is
  a separate, later, human-craft concern.
