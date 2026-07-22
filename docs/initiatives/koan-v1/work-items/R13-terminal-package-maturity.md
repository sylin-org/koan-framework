---
type: SPEC
domain: framework
title: "R13 - Promote the Meaningful Public Surface to 0.20"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: in-progress
  scope: PostgreSQL, SQL Server, and MongoDB public and observed; Couchbase focused evidence active
---

# R13 — Promote the meaningful public surface to 0.20

- Tranche: `T8 — public provider promotion`
- Status: `in-progress — promote Couchbase as the next networked Entity provider`
- Depends on: passed R11, completed R12-06, and accepted
  [ARCH-0120](../../../decisions/ARCH-0120-terminal-package-maturity.md) with validation corrected by
  [ARCH-0121](../../../decisions/ARCH-0121-claim-scoped-validation.md)
- Coordinates with: [R12-07](r12/R12-07-preview-evolution.md); the first newly promoted public
  provider slice supplies its upgrade and recovery observation
- Unlocks: a coherent 0.20 product whose intended providers are installable, supported, and explicit
  about their limits
- Owner: the maintainer-intended public capability families, not a fixed historical package count

## Meaningful outcome

A developer can choose a documented Koan database, vector store, search engine, AI runtime, storage
provider, or authentication provider; install its public 0.20 package; compose it through normal
`AddKoan()` behavior; and receive the promised semantics or a corrective failure.

R13 is not complete because every historical package received paperwork. It is complete when Koan's
intended public provider surface is useful and trustworthy.

## Governing correction

[ARCH-0120](../../../decisions/ARCH-0120-terminal-package-maturity.md) replaces the original fixed
55-owner terminal program with value-led family promotion:

- product intent decides whether a package belongs;
- family and provider evidence decide whether Koan can support it;
- dependency closure ensures a valid foundation but does not measure product value;
- provider adapters are legitimate leaf packages whose consumers are applications;
- promotion uses existing family tests, package/API checks, generated truth, and the main publisher;
- no terminal certificate, universal admission framework, or mandatory owner-count reconciliation is
  part of completion.

The earlier 38/55 inventory remains useful historical discovery. It is not the execution queue.

## Promotion contract

A package or cohesive family moves to a supported claim and project-local 0.20 intent atomically
when one focused promotion slice proves only the claims it changes:

1. a public user guarantee, explicit limits/non-claims, and corrective behavior;
2. shared family semantics plus the provider-specific delta;
3. a real container, runtime, or deterministic protocol boundary when external behavior matters;
4. a successful pack, plus a clean package-only consumer only for first publication or changed
   package/dependency/activation shape;
5. clean pack/API behavior, supported public dependency closure, and current generated product truth.

The promotion PR records exact commands and outcomes. Durable product truth points to the owning
test projects and consumer evidence without centrally duplicating individual test cases.

## Value order

This order expresses expected user value, not ten mandatory waves. Families may move independently
when no real prerequisite connects them.

| Priority | Family | Intended outcome |
|---:|---|---|
| 1 | Entity data providers | PostgreSQL, SQL Server, MongoDB, Couchbase, Redis, and CockroachDB are supported extensions with shared Entity semantics and real provider proof |
| 2 | Vector and search | establish the local vector floor, then promote Qdrant, Milvus, Weaviate, Elasticsearch, and OpenSearch through shared semantics plus native deltas |
| 3 | AI runtime and providers | promote the mainline runtime, Ollama, LM Studio, and ONNX with lifecycle, routing, protocol/runtime, and consumer proof |
| 4 | Storage, backup, and media | promote the local/S3 storage path, integrity-first backup/restore, and media behavior through their real user journeys |
| 5 | External authentication | promote Google, Microsoft, and Discord through deterministic authorization-code protocol tests without requiring live credentials |
| 6 | Accepted migrations | complete only the already-decided Agyo and Zen Garden ownership moves with public destination evidence |

Grouped claims split when providers are not equally ready. A passing adapter does not wait for a
failing sibling, and a failing sibling does not inherit the group's support label.

## Current boundary

PR `#95` merged as `a8a1bb61b53195ce44bef00024d722862deb949d` after the lean gate passed.
Release run `29891926990` published the 45-package supported closure, NuGet.org indexed all seven new
owners, and a clean public consumer passed. The first slice's seven immutable API floors are now
recorded at their owning projects.

[R13-07](r13/R13-07-postgresql-provider-promotion.md) passed through PR `#96`, main commit
`b89cec6266080186db4fdd3fee99aa04b089abbc`, release run `29893491621`, public indexing of both
`0.20.1` artifacts, and a clean NuGet.org-only PostgreSQL consumer. Those exact first versions are
now recorded as immutable API floors.

[R13-08](r13/R13-08-sqlserver-provider-promotion.md) passed through PR `#97`, main commit
`a8d3869adc84d15a330acb52cdf5c7dca916a6ad`, release run `29894829655`, public indexing of
`0.20.1`, and a clean NuGet.org-only SQL Server consumer. Its exact first version is now recorded as
the immutable API floor.

[R13-09](r13/R13-09-mongodb-provider-promotion.md) passed through PR `#98`, main commit
`e90e8fecff4efb3d1a4dd2d956b8d8f1bc4b423a`, release run `29896020354`, public indexing of both
`0.20.1` artifacts, and a clean NuGet.org-only MongoDB consumer that also proved the contract remained
inert. Their exact first versions are recorded as immutable API floors in R13-10.

[R13-10](r13/R13-10-couchbase-provider-promotion.md) is active. Couchbase's complete real-provider
suite passed 20/20 with zero skips against Community 8.0.2. Pack, staged external consumer,
product/API checks, cheap coherence, and the surface ledger are green; publication and public
observation remain. No sibling provider or whole-framework certification is in scope.

## Execution

### R13-A — Simplify the first promotion slice

Status: complete. PR `#95`, lean gate `29891719299`, publication run `29891926990`, public indexing,
and the clean NuGet.org consumer are green.

1. Map PR #95 changes to the retained/removal lists in ARCH-0120.
2. Delete generic R13 coordination that has no smaller existing owner.
3. Keep family conformance, lifecycle, consumer, API, compiler-drift, and actual package corrections.
4. Simplify claims to capability ownership and durable evidence locations.
5. Run focused owner/family tests and required native proof; run a package consumer only when artifact
   shape or first publication makes it meaningful; run product/API checks at PR coherence.
6. Update the draft PR to describe value delivered rather than owner-count reconciliation.

### R13-B — Publish and observe the first lean slice

Status: complete. The seven owners are public and the clean external consumer passed.

1. Merge only after the simplified draft is green and publication is intended.
2. Observe the public packages through clean NuGet consumers.
3. Complete R12-07's bounded upgrade and recovery evidence without adding release machinery.

### R13-C onward — Promote provider families

Status: active through R13-10 Couchbase promotion.

Open one short family card only when implementation begins. Each card freezes its guarantee and
limits, names the existing conformance owner, identifies the real provider boundary, and ends with a
public consumer result. Do not create cards merely to reproduce inventory rows.

## Acceptance

1. The intended provider families are represented by honest supported claims and public 0.20 packages.
2. Each promoted family passes shared semantics, provider-specific behavior, clean consumer use, and
   package/API integrity.
3. Provider documentation states selection, configuration, limits, failure, and unavailable-service
   behavior.
4. Supported claim/version/dependency agreement and generated product truth remain compiler-enforced.
5. Public-feed observation proves the package a developer installs, not a repository project graph.
6. Known migrations or retirements complete only where product ownership has already been decided.
7. No fixed owner-count reconciliation or generic admission subsystem is required for completion.

## Boundaries

- Do not infer value from reverse dependencies.
- Do not promote mechanically or weaken a provider guarantee to obtain a uniform family result.
- Do not require private applications, private artifacts, or live third-party credentials for a
  public support claim.
- Extend an existing family test owner before creating another framework-wide abstraction.
- Keep publication exclusively on the existing resulting push to `main`.
- Reserve the complete green ratchet for an explicitly requested whole-framework milestone; never
  infer it from package promotion or publication.
- Preserve untracked `tmp/` as unrelated user-owned material.
