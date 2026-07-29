---
type: ARCHITECTURE
domain: data
title: "Koan Data Adapter Conformance Initiative"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: initiative structure, dependency model, current adapter discovery, and governing links
---

# Koan Data Adapter Conformance Initiative

This initiative makes the [Data Adapter Development Primer](../../architecture/data-adapter-development-primer.md)
executable across Koan.Data and every shipped Data adapter. It first establishes provider-neutral framework ownership,
then harvests the current SQLite and MongoDB adapters for lessons and replaces both from empty implementations as
complementary gold references, and finally certifies the remaining adapter fleet.

Full adherence means:

- every applicable obligation in the primer has reproducible passing evidence;
- every advertised capability is true on a pinned real provider;
- every unclaimed capability rejects correctively and cannot be reached through an alternate path;
- provider-neutral behavior has one Framework or Family owner; and
- provider differences remain explicit capabilities rather than accidental semantic drift.

It does not mean identical capabilities on every provider.

## Read in this order

1. [CHARTER.md](CHARTER.md) — binding mission, authority, invariants, and session protocol.
2. [NOW.md](NOW.md) — replace-in-place handoff and next safe action.
3. [PROGRESS.md](PROGRESS.md) — sole live work-item ledger.
4. The selected card under [work-items](work-items/TEMPLATE.md).
5. [ACCEPTANCE.md](ACCEPTANCE.md) — the gate that judges the card.
6. [LAUNCH.md](LAUNCH.md) — paste-ready fresh-session launcher.

[ROADMAP.md](ROADMAP.md) records dependency order and phase exits. It never records live status.

## Why two gold references

SQLite and MongoDB exercise different physical realities while sharing the same Koan application contract:

| Gold | What it must exemplify |
|---|---|
| SQLite | embedded relational lifecycle, logical/physical mapping, schema realization, deterministic local operation, and minimal warm-path structure |
| MongoDB | remote document behavior, pooling and resource ownership, routing/readiness, document codecs, bulk/conditional operations, and topology-dependent capability honesty |

Gold is earned by evidence. SQLite and MongoDB may publish different manifests; neither is required to imitate the
other. A provider-neutral semantic belongs in Data only when its meaning survives both physical models.

Both gold adapters are deliberately ground-up replacements. The current implementations may reveal provider facts,
public compatibility obligations, regression cases, performance traps, and negative lessons. They do not supply the
new architecture, classes, helpers, control flow, tests, or fallback paths. Each target implementation is emptied and
rebuilt against the ratified Koan contracts and native provider APIs, then retired and certified atomically.

## Sources of truth

| Question | Authority |
|---|---|
| What behavior is required? | the primer, especially §§6–10 and every stable ID in its ratified catalog/annexes |
| What public API shape has been ratified? | the primer plus human-approved decisions produced by DAC-02 |
| What does the code do? | pinned source plus reproducible tests and provider probes |
| What does an adapter claim? | its executable claim declaration, projected into the evidence packet |
| What is publicly advertised? | [product/claims.json](../../../product/claims.json) and its generated product surface |
| What work is active? | [PROGRESS.md](PROGRESS.md) |
| What makes a card complete? | [ACCEPTANCE.md](ACCEPTANCE.md) |

Existing adapters, tests, promotion work, and assessment prose are evidence or hypotheses. They cannot weaken the
primer or turn a skipped provider test into passing evidence. For the two gold replacements, harvest produces only
provider facts, public compatibility decisions, negative lessons, and black-box cases—not reusable implementation
structure.

## Discovered fleet

DAC-00 re-derives this roster dynamically. The current repository contains:

- nine Entity-persistence adapters: SQLite, MongoDB, InMemory, JSON, PostgreSQL, SQL Server, CockroachDB,
  Couchbase, and Redis;
- seven Vector adapters: InMemory Vector, SqliteVec, Qdrant, Elasticsearch, OpenSearch, Weaviate, and Milvus;
- shared Family seams in `Koan.Data.Relational`, `Koan.Data.Relational.Npgsql`, `Koan.Data.Core.Document`,
  `Koan.Data.Core.KeyValue`, and `Koan.Data.SearchEngine`; and
- existing AdapterSurface and VectorAdapterSurface TestKits plus `scripts/forge-verify.ps1`.

Cache adapters and `Koan.Data.AI` are adjacent consumers, not Data adapter certification targets in this epic.

## Strategic opportunities captured by the program

1. **An executable primer.** Adapter Forge and the shared TestKits become the sole executable projection of the
   primer, including human-ratified annexes. They reference its stable IDs instead of inventing a second contract.
2. **One claim truth.** Runtime capability publication, test applicability, facts, evidence summaries, and product
   documentation derive from one executable declaration rather than manually synchronized tables.
3. **Differential gold testing.** A shared semantic corpus runs against SQLite and MongoDB. Any difference must be an
   explicit capability or a defect.
4. **Family leverage.** Framework and Family RED findings are repaired once. The new gold adapters implement only
   native dialect, SDK, topology, resource, dispatch, and failure-code behavior against those seams.
5. **Honest narrow adapters.** Redis and InMemory can be fully conformant without pretending to support durability,
   native filtering, atomicity, or lifecycle features they cannot prove.
6. **Regression economics.** Dockerless Core/SQLite/InMemory/JSON checks can run per PR; SQLite+Mongo gate merges;
   networked and heavy fault/performance matrices can run nightly and for release certification.
7. **Agent-authored ecosystem.** Once the two greenfield golds and Forge agree, the primer becomes a reliable authoring
   and certification workflow for first-party, community, and agent-authored adapters without implementation archaeology.
8. **A versioned ecosystem contract.** Evidence packets carry a primer/profile fingerprint, allowing third-party
   adapters and future Koan releases to detect stale certification instead of silently reusing it.
9. **Reproducible work before commits.** A sealed base-commit + patch + source-manifest identity permits independent
   certification of authorized uncommitted work without mixing in unrelated worktree changes.
10. **Safe parallel evaluation.** Provider lanes can run concurrently through orchestrator leases and per-adapter
    handoffs while shared Framework/Family changes remain serialized by owner.
11. **A real authoring proof.** Empty-root gold implementations test whether the primer and shared contracts are
    sufficient on their own; behavioral review recovers valid user expectations without turning old internals into
    the new design.

## Completion

The epic completes only when the portfolio card proves that every dynamically discovered adapter has either:

- a green evidence packet matching its public claims; or
- an explicit human-approved non-shipping disposition with its public claim removed or downgraded under a new identity.

No adapter is grandfathered by package age, existing support status, or test count.
