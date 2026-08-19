---
id: ARCH-0127
title: Connector fleet strategy — capability without infrastructure
status: Accepted
date: 2026-08-19
area: Architecture / Data
related: [ARCH-0094, ARCH-0103, ARCH-0120, ARCH-0125, DX-0051]
---

# ARCH-0127 — Connector fleet strategy

## Outcome

Connector work is ranked by **capability added per unit of infrastructure imposed**, not by vendor
prominence. The highest-value connector is one that makes a store an application *already operates* do
something new.

This decision is normative for the [connector fleet initiative](../initiatives/connector-fleet/README.md).
Task prompts cite it; they do not restate it.

## Context

The shipped connector inventory, verified against `docs/reference/package-quality.json`:

| Family | Shipped |
|---|---|
| Data | Cockroach, Couchbase, ElasticSearch, InMemory, Json, Mongo, OpenSearch, Postgres, Redis, SqlServer, Sqlite |
| Vector | InMemory, Milvus, Qdrant, SqliteVec, Weaviate |
| AI | HuggingFace, LMStudio, Ollama, Onnx |
| Storage | Local, S3 |
| Cache | Redis, Sqlite |
| Web auth | Discord, Google, Microsoft, Test |
| Communication | RabbitMq |

Reading the two lists together exposes the pattern this decision is built on. Koan ships **Postgres,
Mongo, and Redis** as record stores. All three have first-class vector modes. Koan exposes none of
them. A developer already running Postgres must adopt a *second* service to get semantic search, when
the store on disk could already answer.

DX-0051 made "does this add a process to operate?" a required field on every recipe. That field is the
ranking criterion made explicit, and it points at a class of connector that is cheap to build and
disproportionately valuable to install.

## Decision

### Rank by infrastructure imposed, not by vendor

In order:

1. **A store already in the inventory gains a capability.** No new service, no new operational
   surface, no new failure mode. `pgvector` on shipped Postgres is the archetype.
2. **A force multiplier.** One connector that speaks a protocol many providers implement beats several
   vendor-specific connectors.
3. **A conspicuous absence with a large install base.** MySQL/MariaDB is absent from eleven data
   connectors.
4. **A vendor-specific connector for a provider nothing else reaches.**

### Promotion can outrank construction

Two capabilities are blocked by *assessment*, not by absence: `Storage.Connector.S3` is shelved, so
there is **no assessed remote storage path at all**, and `Cache.Adapter.Redis` is unassessed, so there
is **no assessed shared cache**. Both already exist as source. Promoting them unblocks production
deployment stories for a fraction of the cost of building anything, and neither is connector
construction. They are tracked as product-claim work, not as fleet tasks.

### A connector without an oracle is not fleet work

Every connector admitted to the fleet initiative must be provable by a conformance kit that already
exists:

- record plane — `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs`
- vector plane — `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/VectorAodbConformanceSpecsBase.cs`
- selection and reporting — `scripts/forge-verify.ps1`

Conformance behavior lives in the shared kit; a provider's test project supplies a host and inherits
the specs. That is what makes a connector safe to delegate: the acceptance criterion is a published
command with a defined exit code, not a reviewer's judgement.

**There is no AI adapter conformance kit.** Hosted AI connectors are therefore excluded from delegated
execution until one exists — not because they lack value, but because nothing could verify the result.

### Hosted AI connectors are gated on egress governance

OpenAI-protocol, Anthropic, and Gemini connectors are the largest single unlock available, and they are
deliberately **not** in this initiative.

Koan owns classification, tenancy, and the model call inside one composition, so it can refuse to send
a classified field to a third party — a guarantee no ordinary connector library is positioned to make.
Shipping hosted connectors first and governance second means retrofitting a fail-closed boundary onto
live users, and it makes the launch "Koan added OpenAI support" rather than "Koan added governed hosted
AI." The governance boundary is an architecture decision that must land first, and it is frontier work.

### Maturity is not granted by merging

A new connector enters as an ordinary shipped package. Whether it carries a product claim is decided by
the claim ledger under ARCH-0120, and the capability map reports what that ledger says. Merging a
connector never promotes it.

## Consequences

- The initiative's first tasks are provable by a command that already exists, so delegation is safe.
- Three vector connectors are cheap to build and remove a service from the user's deployment, which is
  the strongest adoption argument the framework can make.
- Hosted AI stays blocked until the egress decision lands. This is an accepted delay; the alternative
  is an ungoverned surface that is harder to correct than to postpone.
- Each new connector obliges an entry in the capability map and in every recipe whose ingredient list
  it belongs to, because DX-0051 makes recipes the discovery path. A connector nobody can find is not
  shipped in any meaningful sense.

## Provenance — questions already settled

Recorded so they are not re-litigated by an executor:

| Question | Settled |
|---|---|
| Should hosted AI connectors come first, since they unlock most? | No. No conformance oracle exists, and egress governance must precede them. |
| Should a new connector be marked assessed or supported on merge? | No. ARCH-0120 and the claim ledger own maturity; merging grants nothing. |
| Should `pgvector` live inside the Postgres connector package? | No. It is a separate package on the vector plane, matching every other vector connector. |
| Should the executor write a new conformance kit if one seems missing? | No. A missing kit is a STOP condition, not a task. |
| Should S3 or the Redis cache adapter be rebuilt? | No. They exist; the gap is a product claim, tracked outside this initiative. |

## Alternatives considered

- **Rank by vendor prominence.** Rejected: it produces a list dominated by connectors that each add a
  service to operate, which is the cost users actually resist.
- **Fold `pgvector` into the Postgres connector.** Rejected: the vector plane has its own election,
  capabilities, and conformance kit; folding it would make one package answer to two planes.
- **Delegate hosted AI connectors with hand-written tests.** Rejected: an executor-derived expectation
  validates its own bugs. Without a shared kit there is no oracle.
- **Build a new AI conformance kit as part of this initiative.** Rejected: designing an oracle is the
  work that must not be delegated, and it would block every other task behind it.
