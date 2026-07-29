---
type: ARCHITECTURE
domain: data
title: "DAC-15 Ratify Gold Contracts and Empty Implementation Roots"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: gold contract, ownership, empty-root, and human claim decision gate
---

# DAC-15 — Ratify gold contracts and empty implementation roots

| Field | Value |
|---|---|
| Phase / kind | gold / ground-up decision gate |
| Depends on | DAC-10, DAC-20 |
| Unlocks | DAC-11 and DAC-21 after linked shared contract work |
| Primer scope | SQLite/MongoDB provider facts, compatibility decisions, black-box cases, and target profiles |
| Production writes | shared contract work through linked cards; target adapters only to establish empty roots |
| Owner | Framework/Family/Adapter contract ownership plus human product authority |

## Meaningful outcome

SQLite and MongoDB receive complete contracts and empty implementation roots, so their replacements are derived from
Koan decisions and native provider facts rather than inherited adapter architecture.

## Required work

1. Re-pin both harvest packets. Accept only provider facts, public compatibility decisions, negative lessons,
   performance traps, retirement inventories, and black-box cases. Internal type graphs and implementation recipes are
   not design inputs.
2. Freeze one responsibility map: Framework owns provider-neutral decisions; a Family owns repeated mechanics with the
   same meaning, lifetime, and failure boundary; each Adapter owns native translation, dispatch, resources, topology,
   and exact native failures.
3. Complete any missing DAC-04–DAC-08 Framework/Family contract through bounded linked cards and seal one common base.
   No adapter-local workaround may close a shared RED.
4. Obtain human approval for each gold Target/Declined manifest and for continuity of package/assembly identity, exact
   public API, configuration keys, and revalidated provider dependencies.
5. Freeze the allowed implementation inputs: the primer, ratified contract, certified Framework/Family seams,
   provider facts and official documentation, compatibility decisions, negative lessons, and black-box cases.
6. Inventory every current adapter file, type, registration, option, fixture, and test that must disappear. Then empty
   each target implementation root. An explicitly ratified public identity may remain as a name to reimplement, never
   as an old body or compatibility branch.
7. Before authoring, freeze a compact design sheet listing the expected native boundaries. Every eventual runtime
   type, cache, resource owner, background task, dispatch boundary, or abstraction must own a necessary contract,
   repeated native mechanic, or measured hot-path benefit.
8. Run `Test-GreenfieldReplacement.ps1 -AllowPending` against both packet skeletons and seal the common base identity.

## Verification

- Both target implementation roots contain no former implementation body before DAC-11/DAC-21 begins.
- Retirement inventories cover compile items, registrations, factories, options, fixtures, tests, docs, and generated
  authorities; seeded omissions fail validation.
- Shared contracts build and their focused tests pass from the common base.
- Target/Declined manifests and public continuity decisions carry human approval.

## Source descriptor claim correction

**Task:** Make Source Integration's existing pure descriptor project its own conformance claims, so adapters do not
restate the same capability in a second declaration.

**Application intent:** An application chooses a source once; inspection and named-read availability are described
consistently wherever Koan reports or certifies that source.

**Public expression:** No new expression: `Data.Source(...).Inspect()` and registered `Query`/`Scalar` remain unchanged.

**Guarantee/correction:** `DescribeSource(source)` is the single truth for container listing, address resolution,
description, sampling, record results, and registered reads. Runtime facts and conformance selection cannot silently
underclaim those surfaces. Invalid descriptor work still fails during pure description, before provider activation.

**Complete intent surface:** None beyond the existing adapter descriptor; adapter authors no longer duplicate these
claims in `DescribeClaims`.

**Public concepts:** None added. Existing descriptor flags map to existing primer profiles.

**Docs read:** The primer defines the six profiles; architecture principles require one decision owner; this card
permits bounded shared-contract correction; DAC-03 requires runtime and TestKit claims to share one projection.

**Code read:** `DataClaimSet` currently projects only `DescribeClaims`; `DataSourceIntegrationDescriptor` independently
carries the exact source capabilities; Data Core calls both for the selected source; Forge currently binds only six
MongoDB rows and leaves 78 non-vector IDs unbound.

**Reusing:** Existing descriptor enums, profile constants, claim builder, source-aware resolution, and diagnostics.

**Creating new:** No file or public concept is added. Descriptor-to-profile projection lives in `DataClaimSet.cs`;
selected-source arguments flow through `DataService.cs` and `DataSourceIntegrationService.cs`; regression coverage
lives in the existing Data Core diagnostics specs.

**Coalescence:** `DataClaimSet` absorbs this projection. Adapter-local repeated profile declarations become unnecessary.
No emitter, registry, or second capability map is created.

**Ergonomics:** Adapter authors implement one descriptor; facts, diagnostics, and certification discover the same
semantics automatically.

**Constraints satisfied:** No application or HTTP surface changes; stable profile identifiers are reused; description
stays pure and off provider hot paths; no unbounded state or new moving part is introduced.

**Risks:** Descriptors may vary by source, so every call passes the selected source or the already-computed descriptor;
projecting a default descriptor globally would be incorrect.

**Result:** `DataClaimSet` now projects the selected source descriptor without activating the provider and coalesces
an exact explicit claim. Data Core passes the selected source and reuses an already-resolved descriptor. Focused Core
coverage passes 30/30, including scalar-only non-overclaim; MongoDB passes 35/35 against the real provider, canonical
Forge is GREEN, greenfield lineage and all 23 source hashes pass, and the initiative catalog remains consistent.

## Definition of done

- [ ] Both gold contracts and target manifests are ratified.
- [ ] All shared seams required by either contract are implemented and certified on one common base.
- [ ] SQLite and MongoDB implementation roots are empty and their retirement inventories are complete.
- [ ] The allowed input set and minimum-moving-parts rule are frozen for both replacements.
- [ ] DAC-11 and DAC-21 can start without a bridge, shadow registration, or alternate implementation path.

## Stop conditions

An unresolved public decision, missing shared seam, incomplete retirement inventory, non-empty target implementation,
unapproved claim change, or proposed moving part without a contract/hot-path reason blocks both gold branches.
