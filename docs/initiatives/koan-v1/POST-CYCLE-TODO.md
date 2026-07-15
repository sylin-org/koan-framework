---
type: GUIDE
domain: framework
title: "Koan V1 Post-Main-Cycle Todo Register"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: bounded design and polish debt deliberately deferred from active V1 slices
---

# Koan V1 post-main-cycle todo register

## Contract

This register preserves small but real issues that deserve deliberate treatment after the active
V1 slice. It prevents two failure modes: widening the active repair until it never
finishes, and losing a design concern because the immediate warning or symptom was made quiet.

An entry is not authorization to change a public contract. Before implementation, give it a bounded
card, apply the normal exploration workflow, decide compatibility explicitly, and name executable
acceptance evidence. If new evidence makes an entry a correctness, security, or release blocker,
promote it into the active backlog instead of waiting for this list.

## Current register

| ID | Surface | Deferred issue | Why it stays out of the main cycle | Decision required before work | Acceptance evidence |
|---|---|---|---|---|---|
| PMC-001 | Entity / Jobs | `JobMetric.Count` intentionally hides the inherited `Entity<JobMetric>.Count` accessor. The explicit `new` modifier is honest but leaves this Entity without the canonical type-level count experience. | Renaming a public persisted property can change C#, JSON, and stored-field contracts; repair 5 only needed a warning-free supported path. | Choose a compatible migration (`Total`, mapped persisted name, alias/obsoletion window) and decide whether Koan should diagnose Entity members that collide with framework statics/facets. | Jobs behavior suites, persistence migration proof, serialization contract, and a compile-time Entity-collision test. |
| PMC-002 | MCP configuration | `EnableHttpSseTransport`, `HttpSseRoute`, and `SseConnectionTimeout` retain legacy SSE names although they primarily configure Streamable HTTP. | Current names preserve pre-1.0 configuration compatibility; renaming while repairing prose would mix transport behavior with config migration. | Choose canonical Streamable names, alias/obsoletion behavior, configuration precedence, and removal timing. | Options-binding tests for old/new keys, startup-report provenance, Streamable suite, and legacy opt-in suite. |
| PMC-003 | Build quality | The two supported application contracts are warning-as-error clean, but the broader Release solution currently passes with 0 errors and 19 historical warnings. R07-02 introduced no warning-bearing-file delta. | A solution-wide cleanup crosses unrelated modules and should not obscure first-use or streaming acceptance. | Inventory the 19-warning baseline by code and owner; classify correctness, API-doc, dependency, sample, and intentional compatibility warnings before suppressing or fixing any. | Release solution build with a recorded zero-warning target or an explicitly owned temporary baseline that can only shrink. |
| PMC-004 | MCP / Web wire shape | Custom `[McpTool]` results preserve .NET property casing while REST uses Koan Web camelCase. | Changing casing is a client-visible wire decision; current GoldenJourney proof is deliberately tolerant. | Decide whether all application-facing MCP JSON adopts Web serialization, remains CLR-shaped, or versions the behavior. | Golden wire fixtures for custom tools, generated Entity tools, STDIO, Streamable HTTP, and compatibility behavior. |
| PMC-005 | Release tooling | Repository discovery accepts a `.git` directory but not the `.git` indirection file used by linked worktrees. | The verified clean-clone release path works; linked-worktree convenience is not release correctness. | Decide whether all Git worktree shapes are supported and centralize root discovery rather than adding command-specific exceptions. | Release-plan and package rehearsal from a linked worktree plus unchanged clean-clone coverage. |
| PMC-006 | Release tooling | Long package plans capture child output and can appear idle between package completions. | Buffered output does not weaken artifact evidence, but it makes operator supervision unnecessarily uncertain. | Define concise live progress events without leaking secrets or making resumable state depend on console rendering. | A bounded slow-process test proving periodic progress, failure context, and unchanged machine-readable evidence. |
| PMC-007 | Web / adapters | Public REST filtering needs a fresh provider-parity audit: the verified FirstUse example is SQLite-specific, while older adapter-surface notes describe uneven `filter` support and unsafe historical degradation. | Repair 5 needs one truthful query example, not an unbounded provider campaign; retained notes may also predate recent fail-loud work. | Reconcile current code/tests/docs, then require execute-correctly or reject-loudly per adapter without implying universal pushdown. | Shared `GET ?filter=` convergence cases across supported adapters, mutation tests against drop-filter behavior, and current capability facts/docs. |
| PMC-008 | Data / vector transactions | `VectorSaveOperation` crosses the Data.Core→Vector boundary through reflection, method-name lookup, and a runtime `Task` cast; nearby error text still says `UpsertAsync` while lookup uses `Upsert`. | The nullable-array correction is behavior-preserving; replacing the bridge changes a cross-project contract and transaction behavior. | Decide whether a small Core-owned capability seam can remove reflection without reversing dependency direction; otherwise make the reflection contract explicit and fail-loud. | Transactional vector save/delete integration proof, missing/incompatible method mutations, cancellation, and non-atomic reporting. |
| PMC-009 | Documentation tooling | XML documentation defects can remain invisible until a public sample rebuild happens to traverse the owning project. | The supported contracts now reject warnings, but repository-wide doc-link validation belongs with the broader warning policy. | Decide whether packable projects or the solution should treat XML-doc reference warnings as errors and how generated/legacy code is scoped. | A deliberately broken `cref` mutation fails the selected CI lane; current shipping modules pass it. |
| PMC-010 | Public module catalog | `docs/reference/modules-overview.md` is a pre-v0.17 breadth catalog with nonexistent packages, universal coverage language, and blanket NuGet availability. It is now explicitly deprecated rather than silently trusted. | Rebuilding the whole catalog would widen R07-01 from context ownership into a repository-wide package/support audit; the current capability ledger already provides the responsible decision surface. | Retire the duplicate catalog or generate a small package index from packable-project inventory, then link each entry to package-owned truth and evidence without restating maturity claims. | Strict docs plus an inventory-derived link test; no nonexistent package/API rows; support and installation claims resolve to `CAPABILITIES.md` and package evidence. |
| PMC-012 | Mongo connector | The full Mongo suite passes 67/68. Its sole remaining failure is the pre-existing ZenGarden URI preference mismatch (`localhost:27017` versus the expected service endpoint); provider-bounded streaming and mixed-case filter convergence pass. | R07-02 repaired mixed-case binding centrally through canonical field paths, but ZenGarden endpoint election is a separate configuration concern. | Reproduce the URI election failure in isolation, state the intended precedence among explicit configuration, orchestration discovery, host loopback, and service endpoints, then correct the shared resolver rather than special-casing Mongo tests. | Mongo full suite 68/68 plus an isolated endpoint-precedence matrix; Couchbase remains 17/17. |
| PMC-013 | Data/Web Backup | Web operation tracking is process-local and polling-only; its cancel endpoint marks tracker state but does not stop active I/O. Global export is sequential despite a `MaxConcurrency` option, the complete ZIP is memory-resident, and Web Backup has no executable suite. | R07-02 needs one real Backup consumer and truthful public boundaries, not a production recovery subsystem rebuild. | Decide whether to graduate Backup: real cancellation ownership, durable operation state, archive streaming, intentional concurrency, authorization, recovery guarantees, and push notifications must be designed as one operational contract. | End-to-end Web tests prove submit/poll/cancel/restart/authorization; cancellation stops provider work; a large archive does not require full memory; recovery drills prove restore integrity. |
| PMC-014 | Data Backup security | `Encrypt` and `EncryptByDefault` currently record policy intent/manifest metadata only; the archive payload is not encrypted. Public docs now state that boundary explicitly. | Quietly inventing cryptography or key custody inside a streaming repair would be unsafe; retaining an implementation-looking flag indefinitely is also a footgun. | Either remove/rename the metadata-only flags in the next breaking wave, or specify authenticated encryption, key identity/rotation, failure posture, and restore-time key resolution before implementing them. | Known-plaintext archive inspection, authenticated-tamper rejection, wrong/missing/rotated-key cases, redacted facts/logs, and a documented recovery drill. |
| PMC-015 | Entity model naming | CLR Entity models whose public members differ only by case, such as inherited `Id` plus business `id`, do not have one portable physical-field identity across all qualified adapters. R07-02 now resolves exact CLR paths first and preserves unambiguous case-insensitive public filtering, but it does not certify those storage models. | Rejecting or remapping a model changes a public modeling and persistence contract; the streaming repair only needs exact identifier detection and truthful scope. | Decide whether model discovery rejects case-colliding members universally, providers declare case-sensitive naming capability, or an explicit persisted-name mapping can prove injectivity without adapter exceptions. | A case-colliding model rejects before provider I/O with one correction across the adapter matrix; ordinary mixed-case public filters continue to converge. |
| PMC-016 | Release recovery | Same-source replay can reconcile a package push followed by a symbol/state failure. If a later source event advances the lineage first, the workflow does not yet retain or recover the prior wave's exact symbol artifact automatically; package visibility alone cannot prove symbol publication. | R07-03 proves identity compilation and same-wave replay without introducing a second artifact-retention system. Nothing has been published through the new flow, so the cross-event failure must remain explicit rather than being called recovered. | Choose durable pre-publication artifact retention keyed by `VersionCommit`, or an equally exact prior-wave reconciliation protocol. Do not infer symbol success from nupkg visibility and do not mint replacement bits. | Inject nupkg success followed by symbol failure, advance a later source event, and prove the automatic workflow reuses or verifies the exact prior `VersionCommit` artifacts, completes publication/state/evidence without operator input, and never reuses an identity for different bits. |

## Promoted and resolved history

- **PMC-011 — automatic reverse-dependent release closure.** Promoted into
  [R07-03](work-items/r07/R07-03-automatic-package-lineage.md) and resolved on 2026-07-15. The
  evaluated graph, durable Git lineage, complete breaking closure, and independent leaf behavior are
  recorded in that card and in [PROGRESS.md](PROGRESS.md). This history remains here so the original
  release-safety concern cannot disappear when the active register changes.

## Working order

Before the first real automatic publication, resolve or explicitly gate the release-correctness edge
in `PMC-016`. Then start with the inventory-oriented items (`PMC-003`, `PMC-007`, `PMC-009`,
`PMC-012`), because they establish the real size and severity of the work. Discuss
compatibility-sensitive API choices (`PMC-001`, `PMC-002`, `PMC-004`, `PMC-008`, `PMC-014`,
`PMC-015`) and the bounded Backup graduation decision (`PMC-013`) next. Finish with independently
useful release-tooling polish (`PMC-005`, `PMC-006`). Fewer cards may result if one root repair
responsibly closes several entries.

## Closure rule

Remove an entry only when its decision and evidence are linked from `PROGRESS.md`, or when a recorded
review rejects the work as unnecessary. Do not mark an item complete merely because its warning was
suppressed, its documentation was softened, or the active cycle ended.
