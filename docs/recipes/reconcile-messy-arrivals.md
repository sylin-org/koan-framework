---
type: RECIPE
recipe: reconcile-messy-arrivals
title: "Turn inconsistent arrivals into one trusted record"
domain: canon
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/reconcile-messy-arrivals.md
gets_you: "One trusted Entity assembled from several imperfect sources, with the reasoning kept."
works_if: "The same real-world thing arrives more than once, from places that disagree."
costs: "Adds no service. Adds a review path for ambiguity, which needs someone to work it."
ingredients:
  - "one | reconcile imperfect arrivals | Sylin.Koan.Canon"
  - "optional | review and commit over HTTP | Sylin.Koan.Canon.Web"
  - "optional | durable ingestion of arrivals | Sylin.Koan.Jobs"
---

# Turn inconsistent arrivals into one trusted record

Raw arrivals are kept. Matching is deterministic and explainable. The trusted Entity is the output, not
the input.

## When this is the answer

"The same customer comes from three systems with three spellings." "Our CRM and billing disagree."
"We import supplier files and half of them are wrong."

This is a specific and often-misdiagnosed problem. Reach for it when **the same real thing arrives
repeatedly from sources that disagree** and someone must be able to explain why the surviving record
looks the way it does. If arrivals are simply *dirty* rather than *conflicting*, ordinary validation is
the cheaper answer — do not bring reconciliation to a validation problem.

The questions that decide the design:

- **What makes two arrivals the same thing?** This is the whole design. An email, a tax id, a fuzzy
  name plus postcode? Write it down before any code.
- **When the rule is ambiguous, what happens?** Guessing is how bad merges get shipped. The honest
  options are reject or route to a human, and someone must actually work that queue.
- **Who wins when sources conflict?** Most recent, most trusted source, or field-by-field. Say it
  explicitly; "it depends" becomes an unexplainable record.
- **Must a merge be undoable?** Usually yes, once someone sees a wrong one. That requirement shapes
  everything, so ask it early rather than after the first bad merge.

## Assembly

```powershell
dotnet add package Sylin.Koan.Canon
```

Keep the raw arrival, the identity of its source, the matching rule, and the provenance of each
committed field. The provenance is what makes the record defensible later — without it you have a
merged row nobody can explain.

Depth: [Canon how-to](../guides/canon-capabilities-howto.md).

## Prove it

1. **Behavior** — a clean match merges; a genuinely new arrival creates; an ambiguous one goes to
   review rather than guessing.
2. **Composition** — assert provenance survives on the committed record, field by field.
3. **Correction** — replay the same arrival and assert nothing duplicates; assert a commit failure
   leaves the raw arrival intact and re-runnable.

Test the ambiguous case deliberately. It is the one that matters and the one nobody writes.

## Boundaries

- Canon does not invent a matching rule. A bad rule reconciled confidently is worse than no
  reconciliation.
- It does not clean data it was never given.
- A committed record is only as trustworthy as the rule and the review that produced it.

## Interacts with

**Background work.** Ingestion of arrivals belongs in durable work, with the arrival as its receipt.

**Tenancy.** Matching must never cross a tenant boundary — reconciling two customers' records into one
is the worst possible version of a data leak.
