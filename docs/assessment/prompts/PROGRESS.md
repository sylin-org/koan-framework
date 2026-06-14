# Prompt Stash Progress Ledger

One-stop tracking for the assessment's implementation prompts. Each row maps to a
self-contained card file under [`06/`](06/) (tactical) or [`07/`](07/) (strategic).
Canonical sources: [`../06-prompt-stash.md`](../06-prompt-stash.md) and
[`../07-strategic-prompt-stash.md`](../07-strategic-prompt-stash.md).

**Agents**: open your card file, paste it into a fresh session, and update your row here —
status `in-progress` when you start, `done` / `blocked` / `obsolete` (with a one-line note)
when you finish. Link commits by short SHA. If repo reality contradicts the card, record it
in the **Divergence log** at the bottom.

**Tier** (load-bearing — do not feed frontier cards to small models):
`T1` small model, autonomous · `T2` small model, recipe-driven · `T3` frontier model only.

**Status**: `pending` · `in-progress` · `done` · `blocked` · `obsolete`.

## Run order (it's a gated DAG, not a strict sequence)

```text
FIRST:  B1 (sln truth) · F1 (fail-loud boot — improves every later session's error visibility)
THEN:   A1 A2 A3 · C0 · all cut/park cards (C*) · most E* · D* · G* — any order, B1 first for cuts
LATE:   H-series (docs/DX) after A2 lands · B2/B3 (CI/release) after B1
07:     entire strategic ladder gated on B1 + F1 + A2 first; then P1 → P2 → P3 → P4 → P5,
        with hard gates noted per row (P4.1 ← Facet 3; P4.2 ← 06 S3).
```

## 06 — Tactical (truth restoration, cuts, migrations, DX)

Preamble for these cards: the `[PREAMBLE]` block in `../06-prompt-stash.md`.

| ID | Tier | Deps | Status | Date | Agent/model | Commits | Notes |
|---|---|---|---|---|---|---|---|
| B1 | T2 | — (first) | pending | | | | sln truth — add all test projects to Koan.sln |
| B2 | T2 | B1 | pending | | | | CI PR gate |
| B3 | T3 | B1, B2 | pending | | | | NBGV-native release; nuspec metadata fix |
| A1 | T1 | — | pending | | | | ADR status sweep (mark superseded/retired) |
| A2 | T2 | — | pending | | | | front-door drift sweep (ghost APIs, version pins) |
| A3 | T1 | — | pending | | | | repo-root + source-tree litter |
| C0 | T1 | — | pending | | | | wave 0: debris/tombstone directories |
| C1 | T2 | B1 | pending | | | | cut Koan.Data.Cqrs (+ Mongo outbox) |
| C2 | T2 | B1 | pending | | | | cut Koan.WebSockets |
| C3 | T2 | B1 | pending | | | | cut Koan.Web.Json.Strict |
| C4 | T2 | B1 | pending | | | | attic-tag Koan.Web.Connector.GraphQl |
| C5 | T2 | B1 | pending | | | | cut Koan.Recipe.Abstractions + Observability (port ~10 lines first) |
| C6 | T2 | B1 | pending | | | | cut Koan.Service.Inbox.Connector.Redis |
| C7 | T2 | B1 | pending | | | | park Koan.Secrets.* + Vault connector |
| C8 | T2 | B1 | pending | | | | cut Koan.ServiceMesh + Translation service |
| C9 | T2 | B1 | pending | | | | park Koan.Tagging (external downstream consumer) |
| C10 | T2 | B1 | pending | | | | park Koan.Rag + Abstractions (before/with S3) |
| C11 | T2 | B1 | pending | | | | cut Koan.AI dead pipeline surface (not the project) |
| C13 | T2 | B1 | pending | | | | cut PGVector connector + vector filter surface |
| C14 | T2 | B1 | pending | | | | cut Storage ResilientStorageDecorator (file-level) |
| C17 | T2 | B1 | pending | | | | scheduling cut (Koan.Scheduling → jobs) |
| C19 | T2 | B1 | pending | | | | execute MEDIA-0008's overdue deletion |
| C20a | T2 | B1 | pending | | | | merge Swagger connector into Koan.Web.OpenApi |
| C20b | T2 | B1 | pending | | | | merge Koan.Admin into Koan.Web.Admin |
| C20c | T2 | B1 | pending | | | | fold Koan.Web.Transformers into Koan.Web (preserve opt-in) |
| D1 | T3 | — | pending | | | | break the kernel inversion (Core → Orchestration.Abstractions) |
| D2 | T2 | — | pending | | | | delete orchestration stack's dead surface |
| E1-postgres | T2 | B1 | pending | | | | fold Postgres manual registration into registrar |
| E1-mongo | T2 | B1 | pending | | | | fold Mongo manual registration into registrar |
| E1-sqlite | T2 | B1 | pending | | | | fold Sqlite manual registration into registrar |
| E1-sqlserver | T2 | B1 | pending | | | | fold SqlServer manual registration into registrar |
| E1-couchbase | T2 | B1 | pending | | | | fold Couchbase manual registration into registrar |
| E1-json | T2 | B1 | pending | | | | fold Json registration + drop JsonRepo factory |
| E2 | T2 | — (soft: C7) | pending | | | | delete V1 service discovery; rename V2 |
| E3 | T2 | — | pending | | | | one singleflight (hot path — read DATA-0057) |
| E4 | T2 | — | pending | | | | one health aggregator + boot-report surface |
| E5 | T3 | — | pending | | | | auth flow engine swap (OIDC-501 + PKCE + spec) |
| E6 | T3 | — | pending | | | | ES/OS shared core consolidation |
| E7 | T2 | — | pending | | | | slim Messaging.Core + cached typed-delegate dispatch |
| E8a | T2 | — | pending | | | | Postgres BulkUpsert capability token |
| E8b | T3 | — | pending | | | | Couchbase CAS + FastRemove (or ADR note) |
| E8c | T3 | — | pending | | | | Redis-as-data capability decision |
| E9 | T2 | — | pending | | | | EntityContext With(...) doc/code contradiction (docs half) |
| F1 | T2 | — (early) | pending | | | | the fail-loud boot fix (KoanBootException) |
| F2-sqlite | T2 | F1 | pending | | | | swallow burn-down: Connectors/Data/Sqlite (23 sites) |
| F2-data-core | T2 | F1 | pending | | | | swallow burn-down: Data.Core AdapterConnectionResolver |
| F2-web | T2 | F1 | pending | | | | swallow burn-down: Koan.Web (9 sites) |
| F2-storage | T2 | F1 | pending | | | | swallow burn-down: Koan.Storage (9 sites) |
| F2-mcp | T2 | F1 | pending | | | | swallow burn-down: Koan.Mcp (9 sites) |
| G1 | T3 | — | pending | | | | extract Koan.Observability package |
| G2 | T1/T2 | — | pending | | | | cut the kernel's dead strata (FluentApi etc.) |
| H1 | T3 | A1, A2 | pending | | | | dotnet new templates (koan-web / koan-console) |
| H2 | T2 | A2 | pending | | | | snippet lint to 100% + wire into ratchet/gate |
| H3 | T2 | — | pending | | | | docs IA collapse (27 → core dirs) |
| H4 | T3→T1 | — | pending | | | | pillar map cards (first card frontier, rest template) |
| H5 | T2 | — | pending | | | | glossary |
| H6 | T2 | H2 | pending | | | | verb alias sweep (docs only) |
| H7 | T2 | A2 | pending | | | | llms.txt |
| H8 | T2 | H2 | pending | | | | skills refresh + koan-jobs skill |
| H9 | T2 | F1 | pending | | | | boot report: surface failures + provenance |
| S3 | T3 | — | pending | | | | AI pillar consolidation (19 → ~8) — mini-plan + ADR |
| S4 | T3 | E5 | pending | | | | auth + data surface trim — one ADR session |

## 07 — Strategic capability builds (maturity ladder)

Preamble for these cards: the `[SESSION-PREAMBLE]` block in `../07-strategic-prompt-stash.md`.
**Whole ladder gated on 06 B1 + F1 + A2 landing first.**

| ID | Tier | Deps | Status | Date | Agent/model | Commits | Notes |
|---|---|---|---|---|---|---|---|
| P1.1 | T3 | 06 B1 | pending | | | | composition lockfile (koan.lock.json) |
| P1.2 | T3 | 06 F1, P1.1 | pending | | | | runtime introspection over MCP |
| P2.1 | T3 | 06 B1 (· H1) | pending | | | | conformance-by-declaration kits (Sylin.Koan.Testing) |
| P3.1 | T3 | P1.2 | pending | | | | governed agent access — grants, audit, revocation |
| P3.2 | T3 | P3.1 | pending | | | | agent-operable runtime — ops verbs as governed tools |
| P4.1 | T3 | Facet 3 (HARD GATE), P2.1 | pending | | | | multi-tenancy primitive |
| P4.2 | T3 | 06 S3, P2.1 | pending | | | | app-level AI evals |
| P5.1 | T3 | 06 B2 | pending | | | | sovereign / scales-down deployment (AOT) |
| P5.2 | T3 | P1.2, P3.1, 06 H1 | pending | | | | the wedge demo — agent transcript |

## Divergence log

When a pre-flight fails or repo reality contradicts a card, record it here:
date · prompt ID · what was found · what was done instead.

| Date | ID | Finding | Action |
|---|---|---|---|
| | | | |

## Operator gates

When a card surfaces a post-merge action that lives outside the repo (a prod migration, a CI
secret, a manual deploy step), append a `### <ID> operator gate` subsection here describing it,
so the operator has a single place to find pending out-of-band work. None yet.
