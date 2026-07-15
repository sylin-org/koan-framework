---
type: SPEC
domain: framework
title: "R07-01 - Move Ambient Context Beneath Data"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: not-run
  scope: Core typed ambient state and durable carrier ownership
---

# R07-01 — Move ambient context beneath Data

- Tranche: `T6 — semantic capability ring`
- Status: `pending`
- Depends on: ARCH-0113 and R06
- Unlocks: Data semantic truth and context-safe Communication
- Owner: Core context with Data, Tenancy, and Jobs migrations

## Meaningful outcome

Tenant and other module-owned context still survive Jobs and future Communication hops with the same
fail-closed guarantee, but the mechanism no longer belongs to Data. A module can participate in Koan's
logical execution context without pretending its policy is a persistence concern.

Application code remains business-shaped:

```csharp
using (Tenant.Use(tenantId))
{
    await work.Job.Submit(ct: ct);
}
```

The handler observes that tenant before its Entity is loaded and throughout its invocation. Later
settlement retains correlation and a safe context fingerprint, not a live ambient scope.
There is no new application registration step.

## Why now

Events and Transport cannot safely depend on a carrier owned by Data, and duplicating it in
Communication would create the exact isolation drift R07 is intended to remove. The current carrier's
capture/restore/suppress behavior and Jobs tests are strong; this slice relocates and generalizes that
proven contract before adding new consumers.

## Evidence to read first

- `src/Koan.Data.Core/EntityContext.cs`
- `src/Koan.Data.Core/Ambient/IAmbientSliceCarrier.cs`
- `src/Koan.Data.Core/Ambient/AmbientCarrierRegistry.cs`
- `src/Koan.Tenancy/Tenant.cs`, `TenantContextCarrier.cs`, and `TenantAxis.cs`
- Jobs capture/coalescing/orchestrator paths and durable-carrier tests
- ARCH-0100, ARCH-0108, and ARCH-0113

## Decisions

### DECIDED

- Core owns one `AsyncLocal` typed context state, working name `KoanContext`.
- That state belongs to the current logical execution flow, not to a host singleton. An explicit outer
  scope may intentionally span calls into multiple hosts; host-owned services and registrations may
  not.
- Data's current routing/transaction state becomes a Data-owned typed value accessed through the
  existing `EntityContext` facade.
- `EntityContext` is deliberately retained as the Entity/Data operation facade, not as a compatibility
  alias. It no longer owns or exposes generic cross-module context slices.
- Tenancy and other axes use Core context directly. `EntityContext.GetSlice/WithSlice` are removed;
  no compatibility alias remains.
- The carrier contract, registry, exception, and generic tests move to Core and are renamed around Koan
  context rather than Entity/Data.
- Capture remains module-registered, opaque, versioned, deterministic, and allocation-free when empty.
- Restore remains fail-closed for unknown axes and explicitly suppresses absent registered axes.
- Malformed payloads and unsupported carrier versions fail before user code. A syntactically valid
  opaque value proves format, not integrity; authenticated provenance is a separate adapter capability
  when future Communication crosses a process boundary.
- Each carrier declares a generic minimum ingress-trust requirement. Core records the requirement
  without interpreting the axis; Jobs supplies the trust posture of its durable store, and future
  Communication compares it with adapter provenance.
- Data's axis DSL no longer owns `.Carries(...)`; Tenancy registers its Data guard and Core carrier as
  separate responsibilities.
- Jobs retains capture-before-first-await, context-aware coalescing, restore-before-work-item-load, and
  dead-letter-before-handler behavior.

### DEFAULT

- Cache policy remains outside this first move unless extracting the generic Core context makes its
  separation necessary for an acyclic build; any temporary placement is recorded rather than
  advertised as the final owner.

### OPEN

- Exact Core namespace/type spelling after collision and IntelliSense probes.
- Whether a non-public immutable snapshot type is sufficient or a public snapshot is needed by the
  durable-carrier API.

## Scope

### In

- Add the minimal immutable typed context primitive to Core.
- Move and rename the durable carrier seam and registry into Core.
- Rebase `EntityContext` Data state on the Core context without changing routing or transaction
  behavior.
- Migrate Tenancy, Jobs, and every current typed-slice consumer.
- Separate tenant Data-axis registration from durable context carriage.
- Move generic carrier tests to the Core-owned suite and retain all Jobs/Tenant isolation proofs.
- Update dependency descriptions, decisions, XML docs, and runtime facts affected by ownership.

### Out

- Events, Transport, router, envelopes, receipts, or Messaging changes.
- A new unit-of-work/commit coordinator.
- Provider streaming or Lifecycle changes.
- New ambient axes or business features.
- Package publication, push, tag, or release.

## Architecture guardrails

```text
Koan.Core               owns typed ambient state + carrier registry
  ↑        ↑       ↑
Data   Tenancy   Jobs   own their meanings and consume Core
```

- Core may not reference Data, Tenancy, Jobs, Cache, or Communication.
- Data may not name tenant or Communication.
- A carrier owns serialization of its slice; the registry owns only ordering, duplicate-key rejection,
  restore/suppress, and unwind.
- Ingress trust is generic framework metadata. Core cannot name tenant, actor, classification, or a
  connector while enforcing the carrier's declared minimum.
- Unknown carried axes fail before any scope is pushed.
- Context values and carrier payloads never enter general runtime facts, logs, or exceptions.
- Host disposal cannot clear a caller-owned outer logical-flow context, and one host cannot mutate or
  dispose another host's carrier registrations or services.

## Red/green plan

1. Add Core tests that fail because typed context and durable restoration currently require Data.Core.
2. Implement the smallest Core context state and carrier registry that passes nesting, parallel-flow,
   duplicate-key, unknown-axis, malformed/version-invalid, minimum-ingress-trust, suppress,
   partial-unwind, and empty-hot-path cells.
3. Rebase `EntityContext` on that state and run its complete context/transaction suite unchanged.
4. Migrate Tenancy and remove `.Carries(...)` from the Data-axis DSL.
5. Migrate Jobs capture/restore/coalescing and prove concurrent Tenant A/B execution plus absent-context
   suppression.
6. Delete the old Data ambient types and all old namespace references.
7. Run dependency, docs, diff, and privacy gates.

## Verification

- Focused tests: new Core context/carrier suite; Data EntityContext/context/transaction suites;
  Tenancy context/axis suites; Jobs durable-carrier and tenant-idempotency suites.
- Broader regression tests: Core, Data.Core, Tenancy, Jobs, and bootstrap lanes affected by composition.
- Structural checks: no generic slice/carrier implementation remains under Data; no Core reference to a
  higher pillar; no `.Carries(...)` Data-axis API or old carrier namespace remains.
- Documentation / sample checks: strict full-site docs and current code-example gates where affected.
- Manual or observable proof: one isolated host shows the same safe carrier identities without values.
  Two hosts on one logical flow keep registrations/services isolated; a deliberate outer context is
  visible to both, nested restoration does not leak, and disposing either host does not dispose that
  caller-owned scope.
- Privacy check: no private downstream identity, path, persona, or workflow enters evidence.

## Acceptance additions

- Tenant A and Tenant B execute concurrently without cross-observation.
- Work submitted with no tenant executes with the tenant axis explicitly absent, even when drained from
  inside a Tenant scope.
- Unknown, malformed, or version-incompatible context dead-letters before Entity load or handler
  invocation. Absence of a registered axis instead creates explicit suppression.
- Tests do not describe opaque format validation as tamper detection. A future cross-process adapter
  must authenticate provenance before restoring a security-sensitive axis.
- Carrier trust requirements are inspectable by safe identity only; current Jobs restoration proves its
  durable ingress supplies the required trust without exposing context values.
- Existing Jobs records remain readable or receive an explicit greenfield migration decision; no silent
  reinterpretation is allowed.
- Removing the Tenancy module makes its carrier absent and makes retained tenant payloads fail closed.
- The resulting implementation contains one typed ambient state and one durable carrier registry.

## Stop conditions

- Stop if Core would need to reference a higher pillar or interpret an axis.
- Stop if the move requires a second ambient state for cross-pillar slices.
- Stop if any compatibility shim would leave Data and Core as competing context owners.
- Stop if a retained durable payload can execute without its registered carrier.
- Stop before Communication implementation or external publication.

## Session close

Update the parent R07 card, [`../../PROGRESS.md`](../../PROGRESS.md), and
[`../../NOW.md`](../../NOW.md) with exact tests, remaining unsupported scenarios, and the next safe
slice.
