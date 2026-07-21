---
type: SPEC
domain: framework
title: "R07-13 - Pointwise Relationships"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.19.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: direct relationship enrichment over scalar, finite, and provider-bounded Entity sources
---

# R07-13 — Pointwise Relationships

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-12
- Unlocks: the next business-proven secondary capability lift
- Owner: Data relationship metadata, batching, backend negotiation, and graph results

## Meaningful outcome

One direct-relationship operation reads the same for one Entity, a finite business selection, or a
provider-bounded stream:

```csharp
var graph = await order.Relatives(ct);
var graphs = await orders.Where(order => order.Ready).Relatives(ct);

await foreach (var current in Order.QueryStream(order => order.Ready).Relatives(ct))
{
    // one direct graph per source Order
}
```

No caller spells key types, loader types, repositories, provider branches, or batching mechanics.
Every child edge still uses strict backend negotiation unless the caller supplies an explicit finite
`RelationshipQueryPolicy`.

## Architecture

- `Relatives` is the only relationship verb lifted in this child. `GetParent` and `GetChildren`
  remain scalar, surgical edge navigation.
- Scalar `Relatives` remains intrinsic Data vocabulary on `Entity<TEntity,TKey>`; finite and async
  receivers are Data.Core extensions. No decorative `.Relationships` facet or flow container exists.
- The finite/async receiver shape carries `Entity<TEntity,TKey>` in its constructed type so C# infers
  both the model and custom key type from ordinary model sources.
- `EntityCardinality` preserves one-pass enumeration, cancellation, order, multiplicity, and invalid
  source diagnostics.
- One internal `RelationshipGraphLoader` owns bounded source batching. Each parent edge uses one
  `GetMany`; each child edge uses one `IRelationshipQueryExecutor` call per source batch.
- Child execution remains native, resident in-memory, explicitly bounded scan/fallback, or corrective
  rejection. The existing `koan.data.relationship.execution` fact remains the explanation owner.

## Principal deletion

- the public `BatchRelationshipLoader` implementation detail;
- the uninferrable `Relatives<TEntity,TKey>` call shape taught by samples;
- scalar `GetRelatives`, leaving one canonical `Relatives` verb across cardinalities;
- duplicate per-Entity graph orchestration and fixed-batch literals outside Data constants.

## Delight contract

- Application code reads as business selection followed by relationship enrichment.
- Coding agents can infer the complete call from IntelliSense without discovering or inventing a key
  type argument.
- Operators and reviewers retain the same safe provider/mode/rejection facts for every child edge.
- A stream stays lazy, one-pass, source-ordered, and bounded to one internal graph batch at a time.
- No module reference, registration call, repository abstraction, or provider-specific code is added.

## Acceptance

- Scalar, finite, and async `Relatives` compile and execute without explicit model/key arguments.
- String and custom keys preserve source order and multiplicity.
- Parent edges batch keyed reads; child edges batch through the shared executor.
- Strict scan rejection and explicitly bounded success produce the existing corrective/selected facts.
- The source sample uses the natural grammar for scalar, finite, and provider-stream paths.
- Data.Core, its relationship proofs, the affected sample, package metadata, and current docs close
  without release-certification execution.

## Explicit non-claims

- recursive traversal, depth budgets, cycle handling, or graph planning;
- cross-key-type relationships;
- relationship index sufficiency or provider-fleet performance certification;
- collection atomicity or one query covering more than one declared edge;
- Web/MCP request authorization through the app-authority domain method; governed projection remains
  owned by the Web endpoint layer.

## Evidence

- Data.Core's complete Relationships namespace passes 10/10: the pre-existing executor/host cells plus
  scalar, finite, async, custom-key, parent/child batching, strict/bounded fact, and corrective
  cross-key proofs.
- Entity Language passes 22/22. Its positive cell compiles scalar/set/stream and custom-key calls with
  only the normal Data namespace; its negative cell rejects a non-Entity source at `Relatives`.
- `Koan.Data.Core` builds warning-as-error with zero warnings/errors. S1's normal build succeeds and
  compiles the natural scalar/set/provider-stream source; its warning-as-error build remains blocked
  only by the pre-existing unreachable-code warning in `Koan.Web.Extensions`.
- The independently versioned Data.Core 0.19 owner packs with DLL, XML docs, README, symbols, and exact
  internal dependency floors. Inventory remains 112 package owners.
- Documentation lint reports 0 errors / 1579 historical warnings. Stale-surface, diff, and privacy
  checks close with no release-certification run.
- One attempted complete Data.Core project run produced no result inside the four-minute slice bound
  and its orphaned test process tree was terminated. This card makes no fresh full-project pass claim;
  the bounded 10/10 owning matrix is its runtime acceptance evidence.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-16
- Follow-up: inventory constrained Jobs submission as the next candidate; admit a lift only if the
  ledger remains the work truth and partial outcomes stay explicit.
- Reviewer: Codex implementation under maintainer standing approval.
