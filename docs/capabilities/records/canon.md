---
type: REFERENCE
domain: canon
title: "Canon reconciliation"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/records/canon.md - cold-executed against published packages
    (Sylin.Koan.Canon 1.0.7, Streamable HTTP): new arrival canonized with enrichment contributor,
    same-key arrival merged into the SAME canonical record (phone + name applied), replay idempotent,
    invalid arrival refused 422 leaving the store untouched, /api/canon/models discovery. Newest-wins
    default for UNDECLARED properties verified against a source pin - published packages freeze such
    fields on first value unless each declares [AggregationPolicy(Latest)].
---

# Canon reconciliation

Keep every raw arrival, decide whether it matches an existing real-world thing, and commit one
`CanonEntity<T>` whose field provenance explains the result.

## You need

| Piece | Package | Note |
|---|---|---|
| Canonical Entity and pipeline | `Sylin.Koan.Canon` | model the trusted record as `CanonEntity<T>` |
| Review and commit over HTTP (optional) | `Sylin.Koan.Canon.Web` | projects discovered Canon models |
| Durable arrival ingestion (optional) | `Sylin.Koan.Jobs` | make the arrival and processing receipt survive restart |

## The constraint box

> **The constraint:** The application owns identity, conflict, ambiguity, and undo rules. Canon
> commits the canonical Entity, indexes, and audit in an explicit non-atomic order; a later checkpoint
> failure can leave earlier state durable, so Canon reports the checkpoint and does not promise
> rollback or blind-retry safety.
>
> **Default conflict rule: newest wins.** A property without `[AggregationPolicy]` reconciles as
> latest-wins — each arrival's value supersedes the previous one. Declare an explicit policy
> (`First` / `Min` / `Max` / `SourceOfTruth`) only where newest is the wrong answer. Keys declared
> with `[AggregationKey]` are identity, never conflicted fields.

## Decide whether Canon is the answer

| Situation | Route |
|---|---|
| Several sources describe the same thing and disagree | Canon reconciliation |
| One input simply violates a known rule | ordinary Entity validation |
| A match is ambiguous | reject or route to a human; never guess |
| Matching might cross tenants | fail the design review; canonical identity must stay tenant-scoped |

## Leaves

- **Build and ambiguous-case proof:**
  [reconcile messy arrivals](../../recipes/reconcile-messy-arrivals.md)
- **Pipeline contract:** [Canon guide](../../guides/canon-capabilities-howto.md)
- **Runtime contract:** [Canon reference](../../reference/canon/index.md)
- **Runnable exemplar:**
  [CustomerCanon](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/CustomerCanon/README.md)

Replay the same arrival in proof. A trusted record that duplicates on retry is not reconciled.
