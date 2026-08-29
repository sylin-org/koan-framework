---
name: koan-create-adapter
description: Author a new Koan connector/adapter package for a provider Koan does not yet reach (data store, vector index, document store, AI runtime, or another pillar seam), prove it against its family's behavioral conformance oracle, and land it with truthful not-assessed maturity. Use when the target provider is absent from the connector matrix and an agent must research, implement, place, and prove a conformant adapter. For composing existing capabilities into an application use koan; for read-only explanation use koan-explain; for framework migration use koan-upgrade.
---

# Create a Koan adapter

Extend the framework through its own seams: research the target, author a conformant adapter package, pass the family's behavioral oracle against a real provider, and land it with honest claims. The adapter is proven by behavior, never by its author's say-so; merging grants no maturity (ARCH-0120) — every adapter this skill produces ships **not assessed**.

## Scope

This skill owns *new connector construction* in this repository: package placement, adapter implementation, conformance proof, and the obligations that make a connector findable and truthful.

It does not own: framework changes outside an adapter (stop and report the gap), product-claim work (ARCH-0120 ledger — maintainer decisions), or regeneration of generated files (`docs/reference/connector-matrix.md` is generated — note it for regeneration, never hand-edit).

## Workflow

Run the steps in order. Every gate is hard: a skipped gate produces an adapter that is not proven, which is worse than no adapter.

1. **Prove necessity (reuse before build).** Read the [connector matrix](../../../docs/reference/connector-matrix.md) and the [capability map](../../../docs/reference/capability-map.md). If a shipped connector or an ordinary SDK integration in the application already satisfies the outcome, stop — reference it and say so. Record: family, alternatives checked, and the concrete gap.
2. **Research and select the target.** For a provider Koan does not reach, apply all five criteria and record the selection rationale plus why each evaluated alternative lost:
   - free/open-source license;
   - absent from the connector matrix (genuinely new ground — near-names are not the same provider: Couchbase ≠ CouchDB);
   - drivable from .NET (official managed driver, ADO.NET provider, or a plain HTTP wire protocol);
   - runnable for tests (local process, embedded engine, or container — the repo has `Koan.Testing.Containers` fixtures);
   - preferably NativeAOT-viable (managed protocol or P/Invoke; no runtime IL emit, no `dynamic`).
3. **Pick the seam and load its playbook.** Playbooks live as `references/<seam>.md` in this skill and
   are added as seams are proven. What exists today:
   - **`references/data.md` exists — Data (record plane: relational, document, key/value).** Load it.
     It is the complete authoring sequence, the mandatory acceptance checkpoints, and the family
     failure modes.
   - **No playbook for this seam yet — derive it.** Read one *assessed* exemplar connector in the
     family, the owning primer sections of `docs/architecture/data-adapter-development-primer.md`,
     and the family's conformance-oracle location (vector plane:
     `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/VectorAodbConformanceSpecsBase.cs`;
     AI has no shared kit per ARCH-0127 — construct the strongest available behavioral proof from the
     exemplar's test shape and say so in the report). Write what you derived as
     `references/<seam>.md` in this skill so the next adapter does not re-derive it.
   A playbook you follow is also a playbook under test: every place it is ambiguous or wrong, fix it
   in the same change.
4. **Place the package per module-authoring law.** `src/Connectors/<Pillar>/<Name>/` (never `samples/` or `tests/`), package id `Sylin.Koan.<...>` inherited from the root props, exactly one concrete `KoanModule`, `[KoanService]` + `[ProviderPriority]` on the factory, `Provider` as the one canonical id plus declarative `Aliases`. Follow `docs/engineering/adding-a-connector.md` for package mechanics (csproj, version.json, test layout).
5. **Implement against the shared substrate, not around it.** A new adapter owns only provider translation and execution; anything shared (mapping compilation, filter translation, schema orchestration, readiness coordination, source policy) belongs to the pillar or family substrate and is consumed, never copied. One activation route, one native execution route per declared operation.
6. **Pass the family's conformance oracle.** Behavioral conformance against a real provider instance (container or embedded engine — fakes prove framework orchestration only, never adapter claims). Full pass, or an explicit reasoned skip per spec: a provider that genuinely cannot lower a behavior rejects it correctively, and that rejection is the tested behavior. "Skip because hard" is not a reason. Record pass counts and the skip list with reasons.
7. **Run the standing gates.** All are non-skippable:
   - **Truthful maturity:** the adapter ships **not assessed**. No product claim in README, `Description`, facts, recipes, or docs; the capability-map entry and connector obligations rows state not-assessed truthfully.
   - **NativeAOT-clean:** no `dynamic`, no runtime IL emit, no `Reflection.Emit`; Newtonsoft is the canonical serializer; consult `docs/guides/nativeaot-howto.md`. Attempt an AOT publish of a consumer (or the adapter's own probe) and report the result; native dependencies are per-RID claims — an unverifiable RID is reported as blocked, with the blocker, not claimed.
   - **`MakeGenericType` sweep:** on every changed generic contract, sweep repo-wide for reflection construction (`MakeGenericType`, string-based activation) — `grep -rn "MakeGenericType" --include="*.cs"` over the touched type names. The compiler cannot see these call sites; the sweep is the only proof they survived.
   - **Obligations sweep:** capability-map entry with truthful claims, recipe ingredients for every recipe whose ingredient list the adapter belongs to, package README + TECHNICAL, `version.json` (release-train membership), tests registered, and a note that `connector-matrix.md` needs regeneration (generated file).
   - **Staging discipline:** stage exactly the files this task owns. Re-check `git status` immediately before staging; the tree routinely carries unrelated in-flight files that must not be committed.
8. **Report.** Lead with what shipped and its oracle result: pass counts, reasoned skips, playbook fixes discovered while following it, selection rationale, build state (0 errors, its own test project green), AOT outcome, and any deviation with its reason.

## Honest-boundary rules

- Same syntax does not imply backend parity. Declare only capabilities a real-store test proves; every unsupported path rejects correctively before provider I/O.
- A provider that cannot honor a declared isolation, filter, or paging behavior must reject — answering an impossible request with silence or a silent fallback is worse than a wrong answer.
- Readiness that cannot become green is worse than red; a business operation is never a missing-shape probe.
- Cancellation, timeout, and provider failure remain distinct; a failed mutation never becomes success and a failed existence probe never becomes not-found.
- Koan surfaces never take the `Async` suffix (`Save()`, `Query()`).

## Trust boundary

This skill does not publish packages, edit product claims, or alter existing connectors. Promotion of a not-assessed adapter into the supported fleet is a maintainer decision (ARCH-0120/ARCH-0127) and is out of scope by design.
