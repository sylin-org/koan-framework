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
  scope: bounded design and polish debt deliberately deferred until the R05 main cycle closes
---

# Koan V1 post-main-cycle todo register

## Contract

This register preserves small but real issues that deserve deliberate treatment after the current
R05 acceptance cycle. It prevents two failure modes: widening the active repair until it never
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
| PMC-003 | Build quality | The two supported application contracts are warning-as-error clean, but the broader solution still has a historical warning baseline outside their closure. The repair-5 query proof currently surfaces `CS0162` in `Koan.Web.Extensions/Controllers/EntitySoftDeleteController.cs`. | A solution-wide cleanup crosses unrelated modules and should not obscure first-use acceptance. | Inventory by warning code and owner; classify correctness, API-doc, dependency, and intentional compatibility warnings before suppressing or fixing any. | Release solution build with a recorded zero-warning target or an explicitly owned temporary baseline that can only shrink. |
| PMC-004 | MCP / Web wire shape | Custom `[McpTool]` results preserve .NET property casing while REST uses Koan Web camelCase. | Changing casing is a client-visible wire decision; current GoldenJourney proof is deliberately tolerant. | Decide whether all application-facing MCP JSON adopts Web serialization, remains CLR-shaped, or versions the behavior. | Golden wire fixtures for custom tools, generated Entity tools, STDIO, Streamable HTTP, and compatibility behavior. |
| PMC-005 | Release tooling | Repository discovery accepts a `.git` directory but not the `.git` indirection file used by linked worktrees. | The verified clean-clone release path works; linked-worktree convenience is not release correctness. | Decide whether all Git worktree shapes are supported and centralize root discovery rather than adding command-specific exceptions. | Release-plan and package rehearsal from a linked worktree plus unchanged clean-clone coverage. |
| PMC-006 | Release tooling | Long package plans capture child output and can appear idle between package completions. | Buffered output does not weaken artifact evidence, but it makes operator supervision unnecessarily uncertain. | Define concise live progress events without leaking secrets or making resumable state depend on console rendering. | A bounded slow-process test proving periodic progress, failure context, and unchanged machine-readable evidence. |
| PMC-007 | Web / adapters | Public REST filtering needs a fresh provider-parity audit: the verified FirstUse example is SQLite-specific, while older adapter-surface notes describe uneven `filter` support and unsafe historical degradation. | Repair 5 needs one truthful query example, not an unbounded provider campaign; retained notes may also predate recent fail-loud work. | Reconcile current code/tests/docs, then require execute-correctly or reject-loudly per adapter without implying universal pushdown. | Shared `GET ?filter=` convergence cases across supported adapters, mutation tests against drop-filter behavior, and current capability facts/docs. |
| PMC-008 | Data / vector transactions | `VectorSaveOperation` crosses the Data.Core→Vector boundary through reflection, method-name lookup, and a runtime `Task` cast; nearby error text still says `UpsertAsync` while lookup uses `Upsert`. | The nullable-array correction is behavior-preserving; replacing the bridge changes a cross-project contract and transaction behavior. | Decide whether a small Core-owned capability seam can remove reflection without reversing dependency direction; otherwise make the reflection contract explicit and fail-loud. | Transactional vector save/delete integration proof, missing/incompatible method mutations, cancellation, and non-atomic reporting. |
| PMC-009 | Documentation tooling | XML documentation defects can remain invisible until a public sample rebuild happens to traverse the owning project. | The supported contracts now reject warnings, but repository-wide doc-link validation belongs with the broader warning policy. | Decide whether packable projects or the solution should treat XML-doc reference warnings as errors and how generated/legacy code is scoped. | A deliberately broken `cref` mutation fails the selected CI lane; current shipping modules pass it. |

## Working order after R05

Start with the inventory-oriented items (`PMC-003`, `PMC-007`, `PMC-009`), because they establish the
real size and severity of the work. Then discuss compatibility-sensitive API choices (`PMC-001`,
`PMC-002`, `PMC-004`, `PMC-008`). Finish with the independently useful release-tooling polish
(`PMC-005`, `PMC-006`). Fewer cards may result if one root repair responsibly closes several entries.

## Closure rule

Remove an entry only when its decision and evidence are linked from `PROGRESS.md`, or when a recorded
review rejects the work as unnecessary. Do not mark an item complete merely because its warning was
suppressed, its documentation was softened, or the active cycle ended.
