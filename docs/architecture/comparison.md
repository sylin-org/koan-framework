# Architecture Comparison

## Full Capability Matrix

<p><strong>Legend:</strong> 🟦 Excellent · 🟩 Good · 🟨 Mixed/conditional · 🟧 Weak/partial · 🟥 Poor/missing</p>

<table>
  <thead>
    <tr>
      <th>Topic</th>
      <th>EF (Data Mapper)</th>
      <th>Active Record</th>
      <th>Koan (entity-first)</th>
    </tr>
  </thead>
  <tbody>
    <tr><th colspan="4" style="text-align:left;">1) Data &amp; Modeling</th></tr>
    <tr>
      <td><strong>Programming model</strong></td>
      <td>🟦 Persistence-free entities + <code>DbContext</code></td>
      <td>🟨 Simple but persistence-coupled</td>
      <td>🟩 AR ergonomics; entities own CRUD, orchestration offloaded</td>
    </tr>
    <tr>
      <td><strong>Provider abstraction</strong></td>
      <td>🟩 Strong for RDBMS</td>
      <td>🟥 Typically one DB/ORM</td>
      <td>🟦 Polyglot (SQL/NoSQL/JSON/Vector) via logical <em>sets</em></td>
    </tr>
    <tr>
      <td><strong>Set / tenant routing</strong></td>
      <td>🟧 Custom</td>
      <td>🟥 Ad hoc / globals</td>
      <td>🟦 First-class <code>?set=</code> + <code>DataSetContext.With(set)</code></td>
    </tr>
    <tr><th colspan="4" style="text-align:left;">2) Querying, Projections &amp; Performance</th></tr>
    <tr>
      <td><strong>LINQ / composability</strong></td>
      <td>🟦 LINQ → SQL</td>
      <td>🟧 Finder-method sprawl</td>
      <td>🟩 LINQ when supported; projections honored</td>
    </tr>
    <tr>
      <td><strong>Capability detection</strong></td>
      <td>🟨 Partial; translation exceptions</td>
      <td>🟥 Rare</td>
      <td>🟦 <code>QueryCaps</code>/<code>WriteCaps</code> + graceful fallback</td>
    </tr>
    <tr>
      <td><strong>N+1 avoidance</strong></td>
      <td>🟩 Include/ThenInclude</td>
      <td>🟥 Common pitfall</td>
      <td>🟩 Batch relationship navigation (<code>Parents/Children/Relatives</code>)</td>
    </tr>
    <tr>
      <td><strong>Bulk operations</strong></td>
      <td>🟧 Add-ons / raw SQL</td>
      <td>🟧 Manual loops</td>
      <td>🟦 First-class bulk upsert/delete paths</td>
    </tr>
    <tr><th colspan="4" style="text-align:left;">3) API Surface &amp; Developer Experience</th></tr>
    <tr>
      <td><strong>Controllers / endpoints</strong></td>
      <td>🟩 Scaffolding exists</td>
      <td>🟧 Often bespoke</td>
      <td>🟦 <code>EntityController&lt;T&gt;</code>: CRUD, bulk, pagination, filters</td>
    </tr>
    <tr>
      <td><strong>Orchestration layer</strong></td>
      <td>🟦 <code>DbContext</code>/UoW</td>
      <td>🟧 Per-entity ops</td>
      <td>🟩 Endpoint Service centralizes protocol-neutral flows</td>
    </tr>
    <tr>
      <td><strong>Cognitive load</strong></td>
      <td>🟩 Moderate</td>
      <td>🟦 Very low</td>
      <td>🟩 Low start; one pattern scales (Entities → REST/GraphQL/agents)</td>
    </tr>
    <tr><th colspan="4" style="text-align:left;">4) Eventing, Views &amp; AI/Vector (Semantic Pipeline)</th></tr>
    <tr>
      <td><strong>Event-driven flows</strong></td>
      <td>🟧 Needs mediator/outbox</td>
      <td>🟥 Ad hoc</td>
      <td>🟦 Flow: staged sets, canonical/lineage projections, monitors</td>
    </tr>
    <tr>
      <td><strong>Read models / CQRS</strong></td>
      <td>🟩 Common</td>
      <td>🟥 Rare</td>
      <td>🟩 View sets (canonical/lineage) via controllers</td>
    </tr>
    <tr>
      <td><strong>Vector / semantic search</strong></td>
      <td>🟥 External add-ons</td>
      <td>🟥 Rare</td>
      <td>🟩 Built-in vector module; <code>SemanticSearch</code>, <code>SaveWithVector</code></td>
    </tr>
    <tr><th colspan="4" style="text-align:left;">5) Operations, Deployment &amp; Tooling</th></tr>
    <tr>
      <td><strong>Migrations</strong></td>
      <td>🟦 EF Migrations</td>
      <td>🟩 ORM-specific</td>
      <td>🟧 Use store-native tools; runtime capability focus</td>
    </tr>
    <tr>
      <td><strong>Dev infra / compose</strong></td>
      <td>🟩 Common patterns</td>
      <td>🟧 Varies</td>
      <td>🟩 Multi-provider Compose; live provider switching demo</td>
    </tr>
    <tr>
      <td><strong>Escape hatches</strong></td>
      <td>🟩 Raw SQL / compiled queries</td>
      <td>🟩 Raw SQL common</td>
      <td>🟩 Direct SQL / custom controllers while preserving sets/caps</td>
    </tr>
    <tr><th colspan="4" style="text-align:left;">6) Testing &amp; Transactions</th></tr>
    <tr>
      <td><strong>Unit testing ergonomics</strong></td>
      <td>🟦 Pure domain tests; provider stubs</td>
      <td>🟧 Often DB-bound</td>
      <td>🟩 Domain tests stay pure; infra behind entities/services</td>
    </tr>
    <tr>
      <td><strong>Distributed transactions</strong></td>
      <td>🟧 Strong inside one RDBMS</td>
      <td>🟥 Very limited</td>
      <td>🟨 Across heterogeneous stores → eventual consistency via Flow</td>
    </tr>
  </tbody>
</table>

---

## Koan Differentials (The Why)

- **Truly storage-agnostic:** same `Entity<>` code across PostgreSQL, MongoDB, SQLite, JSON/Redis, and vector stores; switch via **sets** without rewiring controllers.
- **Semantic pipeline built-in:** embeddings, semantic search, and streaming pipelines are first-class—not bolt-ons.
- **Flow for projections:** canonical/lineage views, staged sets, monitors → CQRS/eventing without ceremony.
- **Low cognitive load:** AR ergonomics (“one pattern”) from CRUD → events → AI/vector; “Reference = Intent”.
- **Zero-boilerplate APIs:** `EntityController<T>` provides CRUD/bulk/pagination/filters; orchestration centralized and reused by REST/GraphQL/agents.

---

## Quick Snapshot (3-Color Mini Grid)

For slide decks and exec summaries, this is the TL;DR.

**Legend:** 🟩 Good · 🟨 Mixed/depends · 🟥 Weak

| Capability                          | EF  | AR  | Koan |
| ----------------------------------- | --- | --- | ---- |
| Time to first API                   | 🟨  | 🟩  | 🟩   |
| Polyglot storage (SQL/NoSQL/Vector) | 🟨  | 🟥  | 🟩   |
| Multi-tenant & view routing         | 🟨  | 🟥  | 🟩   |
| Event-driven & projections          | 🟨  | 🟥  | 🟩   |
| Semantic/Vector pipeline            | 🟥  | 🟥  | 🟩   |
| Capability detection / fallback     | 🟨  | 🟥  | 🟩   |
| Migrations & schema                 | 🟩  | 🟨  | 🟨   |

> Tip: Put the mini grid in the README; link back to this full page.

---

# Adjacent Tech (Other Ecosystems)

A quick map to similar ideas in other languages—useful for evaluators comparing patterns.

## Active Record–style

| Framework               | Ecosystem      | Why people use it                                         | Where it falls short vs Koan                                             |
| ----------------------- | -------------- | --------------------------------------------------------- | ------------------------------------------------------------------------ |
| **Rails Active Record** | Ruby           | Dead-simple CRUD, conventions, migrations, huge ecosystem | SQL-centric; limited polyglot story; AI/vector/pipeline not first-class  |
| **Laravel Eloquent**    | PHP            | Easy AR ergonomics, queues/events/ecosystem               | SQL-first; document/vector require side systems; no capability-fallbacks |
| **Mongoose (ODM)**      | Node + MongoDB | Low-friction for docs; schema middleware, hooks           | Mongo-only; no polyglot routing; no cross-provider caps/bulk semantics   |

## Data Mapper / Unit of Work

| Framework                         | Ecosystem | Why people use it                                             | Where it falls short vs Koan                                           |
| --------------------------------- | --------- | ------------------------------------------------------------- | ---------------------------------------------------------------------- |
| **Hibernate / JPA + Spring Data** | Java      | Mature, projections/specifications, transactions, migrations  | RDBMS-centric; polyglot = extra stacks; semantic pipelines are add-ons |
| **SQLAlchemy (Core + ORM)**       | Python    | Separation of concerns, composable queries, many SQL backends | SQL-only; doc/vector need separate tech; no ambient “set” routing      |
| **Django ORM**                    | Python    | Great DX, admin, migrations                                   | AR-ish models; SQL-oriented; signals ≠ flow/projections                |
| **Doctrine ORM**                  | PHP       | Proper Data Mapper, migrations                                | SQL-first; polyglot via plugins; no semantic/vector layer              |

## TypeScript / Node Hybrids

| Framework                           | Ecosystem  | Why people use it                                                   | Where it falls short vs Koan                                        |
| ----------------------------------- | ---------- | ------------------------------------------------------------------- | ------------------------------------------------------------------- |
| **Prisma**                          | TypeScript | Strong DX, type-safety, migrations                                  | SQL-only; no document/vector first-class; pipelines via app code    |
| **TypeORM / MikroORM**              | TypeScript | Flexible patterns (AR/Data Mapper), multiple RDBMS (and some Mongo) | Limited polyglot; no capability-introspection; no semantic pipeline |
| **Objection.js / Kysely / Drizzle** | TypeScript | Composable queries, type-safe SQL                                   | SQL-only; events/AI/vector are DIY                                  |

## Go & Rust

| Framework           | Ecosystem | Why people use it                        | Where it falls short vs Koan                          |
| ------------------- | --------- | ---------------------------------------- | ----------------------------------------------------- |
| **GORM**            | Go        | AR-like speed, popular                   | SQL-only; flows/semantic need custom build            |
| **Ent**             | Go        | Schema-as-code, codegen, graph relations | SQL-first; no cross-store semantics                   |
| **Diesel / SeaORM** | Rust      | Type-safe queries, compile-time checks   | SQL-centric; pipelines/eventing/semantic are external |

## Elixir

| Framework | Ecosystem | Why people use it                                                 | Where it falls short vs Koan                                             |
| --------- | --------- | ----------------------------------------------------------------- | ------------------------------------------------------------------------ |
| **Ecto**  | Elixir    | Clear Repo pattern, multi-tenant via prefixes, composable queries | SQL-first; doc/vector/semantic and flow pipelines are app-level patterns |
