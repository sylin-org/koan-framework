---
type: SPEC
domain: framework
title: "R09-07 - Compile Explanation, Correction, and Semantic Change"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: schema-2 guarantee semantics, deterministic startup view, exact HTTP/MCP parity, and bounded model-change evidence
---

# R09-07 — Compile explanation, correction, and semantic change

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-02 host constitution, R09-04 selection receipts, R09-05/R09-06 realization facts
- Unlocks: truthful boot explanation, agent/operator parity, bounded change evidence, R09-08 conformance
- Owner: Core runtime-fact semantics and projection; typed plans/failures remain concern-owned

## Meaningful outcome

A developer sees the same important decisions, guarantees, boundaries, and corrections at startup that
an operator or coding agent reads from `/.well-known/Koan/facts` or `koan://facts`. A reviewer can compare
two exact resolved models without another scanner or telemetry history. Adding this inspectability does
not add application code, configuration, or a universal exception hierarchy.

## Focused discovery and coalescence assessment

**User's business sentence:** “Tell me what Koan decided, what it guarantees, what it deliberately does
not guarantee, how to correct a rejected state, and what application meaning changed.”

**Smallest honest application expression:** unchanged:

```csharp
builder.Services.AddKoan();
```

The normal developer reads the boot report. Authorized operators and agents may read the existing HTTP
and MCP facts. Review tooling compares exact resolved models. No `Explain()` registration, diagnostics
builder, or provider vocabulary enters business code.

### Evidence read

- `KoanFact`, `KoanFactEnvelope`, `KoanFactJson`, and `KoanRuntimeFactStore` already provide one bounded,
  redacted, stable-ID, schema-versioned explanation envelope.
- `WellKnownController.Facts` and `RuntimeFactsResourceProvider` serialize that exact envelope; neither
  recomputes composition. Existing equality proofs pin this useful parity.
- `KoanConsoleBlocks` independently selects Elections by kind, Diagnostics by state, but Guarantees by
  the single `koan.segmentation.realization.active` code. Communication and Jobs now emit equally
  important guarantee facts that therefore appear over HTTP/MCP but not at startup.
- `SemanticDecision`/`SemanticProblem` correctly own activation decisions/problems. Provider selection
  uses `ProviderSelectionReceipt`; hard segmentation uses `SegmentationRealizationDescriptor`; Data,
  Communication, and operation failures retain typed reason/correction contracts. Their meaning is not
  identical and must not be flattened into one universal decision or exception hierarchy.
- `KoanLockfileComparer` already performs bounded deterministic change comparison over exact lock models,
  including modules, direct references, elections, capabilities, configuration keys, and Entities when
  both sides contain those richer sections. It is the semantic-diff substrate; a second fact scanner or
  history store would be duplication.
- `IKoanCompositionContributor`/`KoanCompositionBuilder` remain a reporting-only compatibility path, but
  `KoanCompositionSnapshot` still discovers them through `KoanRegistry` and constructs them with
  `Activator`. The six current contributors mostly read canonical plans, but Data also resolves late and
  Cache can materialize plans. Removing this scanner requires a complete typed evidence-source migration;
  it is not a safe side effect of adding fact semantics.

### Coalescence decision

Use `KoanFact` as the single safe explanation projection, not as the runtime decision authority. Add one
stable `Guarantee` fact kind and one builder verb so concern-owned realization facts declare their meaning
without startup parsing codes. Compile the startup view once from semantic kinds/states and render it;
HTTP and MCP continue to serialize the same envelope.

Retain `KoanLockfileComparer` as the bounded model-diff engine. Its exact lock models already exclude
sessions, timestamps, correlation, and fact collection sequence while comparing stable module,
reference, election, capability, configuration-key, and Entity identities. Prove and document that
existing boundary; do not add an unused second comparison API, persist history, or compare prose.

Corrections remain fields on safe facts and properties on typed failures. This slice proves that a
rejected semantic fact projects the same stable reason and correction to startup, HTTP, and MCP. It does
not require unrelated runtime exceptions to inherit from a framework base.

Specificity:

- framework: fact kind/schema, startup view, exact-envelope comparison/projection;
- capability/pillar: decision, guarantee, boundary, and correction content;
- adapter: mechanism evidence only;
- application: no common-path ceremony.

Disposition:

- keep: fact envelope/store/JSON, exact Web/MCP projection, typed decisions/failures, lock comparer;
- absorb: startup's code-specific guarantee selection into semantic fact kinds;
- rebuild: the startup fact selection as one deterministic view over the envelope;
- defer with an explicit deletion gate: runtime `IKoanCompositionContributor` scanning/Activator and
  post-hoc plan materialization, to the complete evidence-source migration rather than a partial alias;
- reject: universal exception/problem hierarchy, facts as health proof, fact history store, prose diff.

## Guarantee and corrective failure

- Every fact declares stable semantic kind/state; renderers select by those semantics, never a pillar code.
- HTTP and MCP remain exact JSON projections of the host envelope.
- Startup shows selected decisions, active guarantees, and rejected/degraded corrections from the same
  fact instances, with deterministic ordering and no sensitive values.
- A bounded diff compares canonical identity and semantic fields only. It never reports session IDs,
  timestamps, sequence numbers, correlation IDs, credentials, context values, or arbitrary payloads.
- Unknown fact kinds fail deserialization by schema/version contract rather than being silently presented
  as another meaning. Introducing `Guarantee` therefore increments the fact schema.

## Red proofs and deletion list

1. A Communication context guarantee and Jobs context guarantee are absent from the current startup
   Guarantees block even though both exist in the exact envelope.
2. A guarantee with a different code but `Kind=Guarantee` must render; a Discovery fact that happens to
   use the old segmentation code must not be the selection mechanism.
3. Startup, HTTP, and MCP retain the same fact ID/reason/correction for one rejection.
4. Exact resolved models report added, removed, and semantically changed stable IDs deterministically;
   host fact capture/session metadata is not part of that model.
5. Delete the code-specific `SegmentationRealizationActive` startup filter after the kind-based proof.
6. Do not delete `IKoanCompositionContributor` until every one of its six consumers has an immutable or
   explicitly dynamic typed evidence source and the registry/Activator inventory reaches zero.

## Scope

### In

- `KoanFactKind.Guarantee` and fact schema increment;
- one `KoanCompositionBuilder.AddGuarantee` projection verb;
- migration of segmentation, Communication context, and Jobs context guarantee facts;
- one deterministic startup fact view selected by kind/state;
- exact startup/HTTP/MCP correction parity proof;
- focused proof and precise documentation of the existing resolved-model comparer's bounded semantic-change contract;
- source inventory and explicit handoff for the contributor-scan deletion.

### Out

- a diagnostics UI, CLI/workbench, telemetry backend, or persisted history;
- universal `KoanException`, `ISemanticProblem`, provider-error, or result type;
- treating facts as readiness/capability proof;
- public application configuration for explanation;
- full `IKoanCompositionContributor` replacement without the six-source migration;
- release certification, package publication, or remote mutation.

## Execution plan

1. Add red startup selection and semantic comparison proofs.
2. Add the explicit guarantee kind/schema and one builder projection verb.
3. Migrate the three current guarantee producers and replace startup's code filter with the compiled view.
4. Prove reason/correction identity across startup, HTTP, and MCP using the existing exact envelope.
5. Exercise the existing full-model comparer for elections/capabilities and document its bounded use;
   add no history owner.
6. Sweep for code-specific render filters, alternate JSON writers, sensitive fields, prose comparison,
   and remaining runtime contributor construction.
7. Update the current handoff and next deletion/conformance slice.

## Focused verification

- Core runtime-fact kind/schema/round-trip and bounded comparison specs;
- Bootstrap startup fact-view specs;
- segmentation composition facts;
- Communication and Jobs context guarantee facts;
- exact Web facts and MCP facts projection specs;
- `KoanLockfileComparerSpec` richer-model cells;
- strict diff/privacy/source inventories; no broad release suite.

## Stop conditions

- Stop if the fact envelope starts making runtime decisions or health claims.
- Stop if startup, HTTP, or MCP needs to reconstruct a pillar plan.
- Stop if semantic comparison requires persisted runtime history or prose parsing.
- Stop if a typed pillar failure must lose fields/behavior to fit a common exception.
- Stop before partially replacing the contributor scan with a second registration path.
- Stop before any publication, push, tag, release, remote mutation, or private downstream disclosure.

## Implementation closure

- Fact schema 2 adds `KoanFactKind.Guarantee`; `KoanCompositionBuilder.AddGuarantee` is the one
  reporting verb for value-free guarantees from canonical concern plans and realization receipts.
- Segmentation, Communication context, and Jobs context facts now declare guarantee meaning explicitly.
  Their stable codes and summaries remain concern-owned; Core does not infer tenant, topology, ledger,
  confidentiality, or delivery semantics.
- `KoanStartupFactView` compiles Decisions, Guarantees, Diagnostics, and module failures once from the
  exact host envelope with deterministic ordering. `KoanConsoleBlocks` renders that view and no longer
  recognizes a specific segmentation fact code.
- Startup therefore includes Communication and Jobs guarantee/boundary facts automatically. HTTP and
  MCP remain byte-identical `KoanFactJson` projections and now pin schema 2.
- Rejected facts retain one reason/correction object across the startup renderer and exact machine
  envelope. Typed pillar exceptions remain typed; no universal exception hierarchy was introduced.
- `KoanLockfileComparer` remains the bounded semantic-change engine. Its 11 focused cells prove stable
  added/removed/changed identities across modules, references, elections, capabilities, configuration
  keys, and Entities. Runtime fact prose/session/timestamp/correlation never enters that model.
- Public documentation no longer teaches application/connector implementations of the reporting-only
  `IKoanCompositionContributor`. Its six framework sources and registry/Activator construction are
  explicitly handed to R09-08 for one complete generated evidence-source/conformance migration.

## Focused execution record

| Cell | Result |
|---|---|
| Core facts + segmentation + resolved-model comparison | 22/22 passed |
| Bootstrap startup guarantee/correction view | 5/5 passed |
| Communication context guarantee kind | 1/1 passed |
| Jobs context guarantee kind | 1/1 passed |
| Web exact schema-2 facts | 1/1 passed |
| MCP exact schema-2 facts | 1/1 passed |
| Changed-document lint | 0 errors; repository-policy/version warnings remain pre-existing |

No release-certification suite, publication, push, tag, release, or remote mutation ran.
