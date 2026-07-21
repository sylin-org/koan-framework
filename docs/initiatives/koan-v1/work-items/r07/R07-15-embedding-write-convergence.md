---
type: SPEC
domain: framework
title: "R07-15 - Embedding Write Convergence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.19.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: AI pointwise inventory and lifecycle-to-vector write convergence
---

# R07-15 — Embedding write convergence

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-14
- Unlocks: the next narrow capability inventory
- Owner: Data.AI lifecycle intent, embedding provider negotiation, vector-only persistence, and state
  confirmation

## Meaningful outcome

The AI inventory rejects syntax that would duplicate an existing business meaning. Ordinary indexing
remains the shortest supported path:

```csharp
[Embedding(Properties = [nameof(Title), nameof(Description)])]
public sealed class Product : Entity<Product>;

await product.Save(ct);
```

Lifecycle, deferred worker, and explicit migration now cross the lifecycle-to-vector boundary through
one internal writer. The application sees one business operation; framework internals do not re-save
the domain Entity or independently reproduce provider routing, model protection, provenance, vector
persistence, and state confirmation.

## Semantic election

- No public `.Index()` alias is added. It would be a second spelling for `[Embedding]` + `Save()`.
- No source `.Embed()` terminal is added. Scalar `EntityAi.Embed(entity)` is an on-demand transform;
  no real application consumer currently proves a collection result/failure contract.
- Explicit finite-set and whole-collection rebuilds remain `EmbeddingMigrator` control-plane
  operations with aggregate partial outcomes.
- A future source operation must prove distinct application intent, bounded enumeration, provider
  negotiation, cancellation/failure semantics, and a real consumer before entering IntelliSense.

## Architecture

- `EmbeddingWriter.Describe` is the one text/signature preparation boundary.
- `EmbeddingWriter.Write` resolves the effective model/source, scopes the embedding provider, embeds,
  guards the vector model, stamps provenance/filterable metadata, writes only the vector, and confirms
  `EmbeddingState`.
- Lifecycle retains save-time intent, the worker retains durable retry/rate-limit intent, and the
  migrator retains explicit rebuild intent. Coalescence does not blur those responsibilities.
- Deferred jobs store Entity identity, content signature, and opaque logical context only. The worker
  restores context, reloads the current Entity, and prepares current text at execution time.
- `EmbedOptions.Source` now scopes the embedding category for its provider call, matching its public
  contract.
- Throughput, rate limit, and retries remain host-level `EmbeddingWorkerOptions`; unused per-Entity
  attribute knobs and duplicated per-job provider/policy fields are removed.

The vector write can succeed before `EmbeddingState` confirmation fails. There is no cross-store
transaction or rollback claim; retry and reconciliation must treat the vector record as possibly
newer than its state sidecar.

## Principal deletion

- three independent embedding/vector/state write implementations;
- the worker's recursive domain-Entity `SaveWithVector` path;
- durable queued embedding text and duplicated model/type/retry/priority policy;
- inert per-Entity batch-size and rate-limit settings; and
- a speculative public Entity source grammar.

## Delight contract

- Developers declare embedding intent once and continue reading `Save()` as business persistence.
- Coding agents have one canonical ordinary-index path and one explicit rebuild path instead of
  choosing among synonyms.
- Operators retain durable job status, retries, cost/latency telemetry, model protection, provenance,
  and migration outcomes while the queue stores less sensitive business material.
- Provider choice is honored at one call boundary and remains inspectable through the existing AI
  routing/fact surfaces.

## Acceptance

- synchronous lifecycle indexing does not recursively re-save the domain Entity;
- deferred indexing reloads the current Entity under restored context and also does not re-save it;
- lifecycle, worker, and migrator produce vectors through the same model/provenance/state boundary;
- an explicit `EmbedOptions.Source` controls the embedding-category provider scope;
- changed public assemblies build warning-as-error and pack with correct independent versions;
- public/project/skill/architecture guidance describes the elected path and cross-store non-claim; and
- focused module, sample, package, docs, diff, and privacy gates pass without release certification.

## Evidence

- Data.AI passes 87/87; AI Unit passes 158/158. Focused source-scope and lifecycle/no-resave proofs
  are included in those totals.
- `Koan.Data.AI` builds warning-as-error with zero warnings/errors. S5.Recs builds with six recorded
  warnings outside this slice and no errors after the inert attribute setting is removed.
- `Sylin.Koan.AI` packs at 0.18.1 and `Sylin.Koan.Data.AI` at 0.19.0 with their README, assembly, XML
  documentation, and exact internal dependency floors.
- Package inventory remains 112 independently versioned owners. Docs lint reports 0 errors / 1581
  historical or front-matter warnings; skills lint passes 20/20 with zero warnings; changed marked
  examples compile 2/2. Diff and privacy gates close without a release-certification run.

## Explicit non-claims

- vector-write/state-confirmation atomicity or rollback;
- completion of deferred indexing when the Entity `Save()` returns;
- collection atomicity or automatic retry in migration operations;
- safe inclusion of classified fields without the classification scrub-or-deny contract;
- a public `.Index()` or source `.Embed()` operation; or
- promotion of AI/vector package maturity.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-16
- Follow-up: inventory the next narrow candidate; add no public capability for symmetry alone.
- Reviewer: Codex implementation under maintainer standing approval.
