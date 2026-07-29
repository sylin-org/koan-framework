---
type: ARCHITECTURE
domain: data
title: "DAC-49 Ratify the Vector Conformance Annex"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: passed
  scope: human-ratified Vector public semantics and stable V-01 through V-24 primer annex
---

# DAC-49 — Ratify the Vector conformance annex

| Field | Value |
|---|---|
| Phase / kind | vector / audit and human decision |
| Depends on | DAC-03 |
| Unlocks | DAC-50 |
| Primer scope | current Source Core plus proposed Vector annex |
| Production writes | no code; primer/decision/initiative artifacts only after human approval |
| Owner | Data public contract and primer conformance catalog |

## Meaningful outcome

A developer and adapter author have one human-ratified source of truth for Vector behavior before any conformance code
or provider evaluation is written.

## Required work

1. Read the current Vector public surface, all seven adapters, VectorAdapterSurface, facts/docs, and DAC-02 governance
   decision. Record current behavior as evidence, not normative authority.
2. Research official provider/client prior art and run bounded probes for score/distance, filtering, dimensions,
   consistency/settling, collection lifecycle, and failure behavior. Start the proposal with smallest user-delight
   examples; provider vocabulary informs corrections and evidence, not Koan's public model.
3. Map every Vector entry path to the primer's exact Source Core rows and optional policy/inspection profiles. Do not
   label conditional cells as Source Core and do not silently skip Entity-inapplicable rows.
4. Draft a primer annex covering save/upsert, fetch, delete, deterministic `topK`, score/distance meaning, filters,
   dimensions/model identity, partitions/source axes, collection lifecycle, consistency/settling, cancellation,
   isolation, disposal, and failures.
5. Propose stable Vector acceptance IDs inside the primer's single conformance catalog, with applicability and evidence
   kinds. Do not create a second normative profile document or machine-readable semantic catalog.
6. Present the public examples, guarantees, corrections, concept cost, and exact IDs to the human. Resolve every
   score/order/filter/lifecycle ambiguity before updating the primer and its decision record.
7. After approval only, amend the primer, freeze its content fingerprint, and hand exact cells to DAC-50. Do not edit
   Forge, TestKit, provider code, claims, or product surface.

## Verification

- Primer/catalog integrity proves unique IDs and one applicability authority.
- Docs/examples lint passes and every Vector operation has an applicable, declined, or provider-native disposition.
- The recorded human decision precedes the primer amendment and no executable conformance code changes.

## Definition of done

- [x] The Vector annex and public ergonomics are explicitly human-ratified.
- [x] Source Core uses the primer's exact definition; conditional profiles remain conditional.
- [x] The primer remains the sole normative acceptance catalog.
- [x] DAC-50 receives a pinned annex fingerprint and finite test/evidence projection.

## Decision

On 2026-07-27 the product owner authorized autonomous implementation of the complete framework and adapter changes
after reviewing the exact ten-item ballot in `evidence/framework/DAC-49-vector-annex-proposal.md`. The ballot is
ratified as written. Its user examples, profiles, and V-01 through V-24 cells now live normatively in the primer; the
proposal remains decision evidence and is not a second catalog.

## Stop conditions

Stop whenever Vector semantics would change public behavior, the human has not approved an exact choice, or an adapter
implementation is being used as normative authority.
