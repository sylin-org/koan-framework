# Analytics feature-class research — user satisfaction with the module's ancestors

> **Preserved research** (2026-08-27). Input to [DATA-0123](../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md).
> Four parallel web-research tracks stress-testing the planned module's pillars against real
> user sentiment: (1) semantic layers / metrics-as-code, (2) materialization & freshness
> systems, (3) in-app & embedded analytics, (4) AI-agent analytics trust. Practitioner voices
> (G2 ratings, Reddit, Hacker News via Algolia, GitHub issues, postmortems) were prioritized
> over vendor marketing; Reddit evidence comes via search-index excerpts where direct fetch was
> blocked. Sources per section.

## Track 1 — Semantic layers and metrics-as-code (LookML, Cube, dbt SL, Lightdash, Malloy)

### Delights (the only themes positive everywhere)

1. **Define once, never argue again.** The single universal positive. MetricFlow Show HN: "A bit
   like generics, but for queries"; HN: "You'd be surprised at how many ways 'active customer'
   can be represented as SQL."
2. **Metrics-as-code with Git/PR/CI governance.** Looker's source control was "the only
   semi-reasonable thing" in its class; dbt's "same repo, same PR, same CI/CD" is its most
   praised property; CI compile tests "catch breaking changes before executives see wrong
   numbers."
3. **Serving metrics through APIs** (Cube's killer feature) — one governed contract feeding
   dashboards, embedded apps, and agents; the reason Cube owners tolerate its ops costs.
4. **Central-gate trust** — reviewed pipelines prevent queries that "bring a DB to its knees";
   Google markets Looker's layer as cutting gen-AI data errors by up to two-thirds.

### Pain points (rough frequency order)

1. **The definition author is a bottleneck — and is almost always an engineer.** "Generally the
   people writing lookml were still engineers"; "a human must manually define every metric
   before a question can be asked," leaving an unmodeled long tail "unanswerable without
   falling back on the data team" (MotherDuck). Most repeated complaint in the track.
2. **Proprietary-declarative-language tax.** Steep learning curve dominates criticism on every
   review platform (38 G2 mentions for Looker alone); "another disparate YAML definition...
   too many places to put YAML"; "Anything but a markup language / JSON."
3. **Declarative ceiling → SQL escape hatches.** "The frontend is so tightly tied to the Looker
   BI stuff"; Cube "feels incomplete... partly by design"; missing window functions and lost
   custom aggregations in MetricFlow; rigid schemas push teams to raw SQL, which drifts.
4. **Ops/performance burden the marketing hides.** Cube Store to run + 507 pre-aggregation
   GitHub issues; dbt SL's compile-latency wall ("78% of teams that ignored latency hit a
   latency wall in week 3–4"; P95 4.8s → 1.1s only after metric whitelisting + catalog cache).
5. **Catalog/model drift and decay.** Static layers "most accurate the day they're built, then
   slowly decay"; Cube cannot detect drift between coexisting dbt/Snowflake/BI definitions.
6. **Cloud-gating and open-core betrayal.** dbt SL API is Cloud-only ($40–95K/yr reported for
   10–20 devs); MetricFlow license anger produced a protest fork; "SMB data teams may feel
   betrayed."
7. **Lock-in** — "Once you're knee deep in LookML you'll have a hard time switching."
8. **Two-source-of-truth duplication** (dbt models vs LookML views; dbt repo vs Cube repo) —
   the original sin the semantic layer promised to fix, recreated by stacking layers.

### Abandonment modes and the economic verdict

- Teams are rarely "ripped out"; they grow out of or price out of Looker; dbt SL pilots get
  **shelved** (parity failures: BI vs semantic API totals disagreed in 6 of 9 rushed pilots).
- **Bottleneck math kills adoption:** if every new question needs a PR to a specialist, users
  keep a shadow spreadsheet — and once the shadow layer exists, the semantic layer's reason to
  exist quietly dies.
- **Casualties:** Transform (absorbed by dbt, 2023), dbt legacy metrics (deprecated),
  MetricFlow standalone (de-standaloned), Metriql (no commit since Mar 2023), Y42 (re-pivoted
  to orchestration). Malloy: alive but marginal; "LLMs are not great at writing Malloy."
- **Benn Stancil (Aug 2025):** standalone layers stalled "less for technical reasons than for
  economic ones — **horizontal layers are hard to sell without owning the application**."
  MotherDuck's counter-essay (Aug 2025) calls the layer "a search problem," warns of "a new
  priesthood of Universe Designers."

### Track 1 sources

- HN 30938109 (MetricFlow Show HN); HN 34724207 (dbt acquires Transform); HN 37621748 (OpenSemanticLayer protest fork); HN "Why Semantic Layers Matter" (Aug 2025, story 44953575); HN/Lightdash + Malloy + LookML Algolia corpora
- G2 Looker reviews; G2 Cube (~4.3/5); Capterra Cube Cloud; checkthat.ai Looker aggregation
- github.com/dbt-labs/dbt-core discussion #7456; infinisynapse dbt SL desk log (18 pilots); unwinddata dbt SL vs Cube; davidsj.substack buyer's guide
- Medium "The dbt Metrics Layer Is Dead"; Medium "$3M modern data stack replaced with Postgres and Python"
- motherduck.com "Who needs a semantic layer anyway" (Aug 2025); promptql.io "semantic layer is dead, long live the wiki" (Dec 2025); benn.spicytakes.org "The Context Layer"; atlan.com Cube limitations
- api.github.com metriql (last push 2023-03-29); y42.com; r/dataengineering semantic-layer threads (incl. Oct 2025 "renaissance"); getdbt.com blog (Looker vs dbt modeling; Fusion/MCP announcements); datafold.com Lightdash post; cube.dev Looker alternatives; docs.getdbt.com SL performance; omni.co + tasman.ai Looker-migration guides; cloud.google.com Looker gen-AI post

## Track 2 — Materialization and freshness systems

### The headline verdict

The "always-fresh vs declared-fresh" debate recurred across systems and **landed in favor of
declared freshness**: the streaming-MV wave (Materialize) validated the desire for
automatically maintained derived data but was punished on cost/memory/licensing (Materialize
went cloud-only; RisingWave stayed a lean niche). Practitioners across the Materialize launch
thread: analysts "didn't want sub-minute data" and "5-10 minute delays" were "perfectly
happy"; always-fresh dashboards can breed "organizational ADHD." **The stated demand is not
"always fresh" but "declared freshness, honestly surfaced, with failures made visible."**

### System findings

- **Looker PDTs + datagroups** — literally "declare staleness, the system maintains a
  materialization," and the clearest cautionary tale about *semantics*, not concept: datagroup
  confusion is endemic (cache-expiry vs rebuild-trigger can't be decoupled in one datagroup);
  33-PDTs-at-3AM thundering herds; silent skip/failure modes ("build skipped due to error in
  required child"; without "Retry Failed PDT Builds," no retry until next trigger); consultant
  audit finding: "Most Looker instances either have no caching strategy or overly aggressive
  caching (users see stale data and don't trust the numbers)" — stale users bypass the tool
  entirely. Best practice converges on declared freshness layered by cadence + cheap triggers
  ("the best trigger is SELECT MAX(completed_at) FROM etl_runs") + monitored hit rates
  (70–90% healthy; <50% = "your datagroup strategy needs work").
- **ClickHouse materialized views** — speed delight ("you don't need to be an expert"; one MV
  powered all dashboards behind a text-to-SQL product) vs a textbook list of implicit-update
  surprises: join views only trigger on the left-most table; backfills need manual re-inserts
  (GlassFlow exists purely to sell this hole); ReplacingMergeTree dedup is eventually
  consistent ("slow FINAL queries or inaccurate results"); inserts slow down; refresh-failure
  debugging needed its own tooling (EXPLAIN MV, system.view_*, Altinity webinars).
- **Materialize / RisingWave** — technically admired, narrowly adopted; cost-control horror
  story in the other direction: a 1992 batch report still burns "tens of thousands of dollars"
  a year computing a report nobody reads (users *want* declared materialization to discipline
  compute). RisingWave production user: "a much better alternative to Kafka Streams."
- **dbt incremental models + source freshness** — the workhorse, loved and endured:
  incremental strategies are "advanced usage" with "many ways to do it wrong" (late-arriving
  data, duplicate rows on insert_overwrite backfills, lookback-window guesswork); freshness
  tests are "noisy"; the war story behind dbt-core discussion #5103 — a team discovered
  Fivetran hadn't synced a table **a full week in**. dbt's jtcohen6 articulated the desired
  semantic there: model latency is "greatest(source_staleness, model_staleness)" — **declared,
  composed freshness budgets**. Snowflake Dynamic Tables' `target_lag` is the production
  version of "declare staleness" — with docs forced to warn "target lag is a staleness target,
  not a guaranteed latency bound and not a refresh schedule," and practitioners bolting
  external triggers on to tame refresh cost.
- **Cube pre-aggregations** — "from a 5-minute loading time to under a second"; but hit-rate
  fragility (a rollup matches Jun 1–Aug 31 but misses Jun 1 11:00–Sep 1 11:00), window
  functions can't hit pre-aggregations at all (#8487), refresh failures (#9584, #10096), and
  Cube's own "major pitfall": "not having detailed insight into when pre-aggregation builds
  are triggered and how long they took."

### Cross-system synthesis

- **Delights:** sub-second answers at scale (universal); cost control; predictable load;
  genuine appetite for writing *what freshness I need* instead of orchestrating *when things
  run*.
- **Pains (cross-system):**
  1. **Silent staleness is the #1 trust killer.** No system surfaces "this answer is X hours
     old" by default. The Rails team that deleted all its materialized views after 8 months
     "due to no longer being able to count on your db queries to return the latest
     information" (HN 22066195).
  2. **Backfill / late-arriving data is the universal hole** (ClickHouse manual re-inserts;
     dbt duplicates; Cube updateWindow; GlassFlow as a product).
  3. **Refresh-failure debugging is opaque** (Looker stuck builds; Cube's blind builds;
     Postgres MV refreshes that "risk blowing your write instance up").
  4. **Coupled, confusing freshness semantics** (Looker's two-datagroup workaround is demand
     in disguise; Snowflake's warning label).
  5. **Configuration burden scales badly** (build strategies, variant explosion, -State/
     -MergeState ceremony); invalidation triggers can cost more than they save.
- **What users explicitly wish existed:** freshness as a first-class, queryable property of
  the answer; decoupling "when data is rebuilt" from "how old an answer may be"; built-in
  catch-up/backfill; **"declare the latency target, the system decides execution"** (Fivetran's
  georgewfraser: "the only thing you care about is the latency target"); build/staleness
  observability.

### Track 2 sources

- reddit.com r/Looker (PDT misunderstanding; trigger rebuilds); discuss.google.dev datagroup + incremental-PDT + BigQuery PDT threads; docs.cloud.google.com/looker derived-tables + PDT troubleshooting; labs4change.com Looker caching
- HN 32496540 (PolyScale/CH MVs); HN 22359769 (Materialize launch); HN 33067345 (Materialize next-gen); HN 22066195 (Rails MVs); HN 26217911 (pipeline as MV); HN 44999194 (MVs obviously useful); HN 44569367 (RisingWave 2025); glassflow.ai alternative-MVs post; clickhouse.com refreshable-MV docs + Mintlify case study; r/Clickhouse 100B-row refreshable thread; ClickHouse issue #78071
- github.com/dbt-labs/dbt-core discussion #5103; docs.getdbt.com freshness + incremental docs; r/dataengineering incremental threads (4); docs.snowflake.com target-lag + dynamic-tables best practices; LinkedIn Snowflake refresh-cost post
- community.embeddable.com pre-aggregation miss; cube.dev pre-aggregations deep-dive; cube-js issues #9584, #10096, #3469, #8487, #10244; SO 60195250; LinkedIn dbtips pre-aggregation post

## Track 3 — In-app/ORM analytics and embedded/customer-facing analytics

### ORM-level aggregation (Django / Rails / Laravel / EF Core)

- **Delights:** zero infra (push work to the DB through models you have); typed and
  refactorable (strongest in EF/LINQ — compile-checked, follows renames); "good enough for a
  long time" — sub-million-MAU products "will be fine" (Trench author on HN).
- **Pains (concrete, evidenced):**
  1. Aggregate query pathology: Django `distinct()+annotate()` ~200ms → 8–9s (r/django); a
     dashboard ListView with six `Count(distinct=True)` annotations ran ~2s unfiltered but
     **3 minutes** with a search term (Django Forum, EXPLAIN cost 750 → 478,000); Laravel
     `withCount()` joins when it shouldn't (framework#18109) and multi-withCount needs
     hand-indexed FKs; EF Core client-eval GroupBy "pulls thousands of rows into memory — a
     nightmare at scale" + a ~10x GroupBy regression (dotnet/efcore#29593) + hidden N+1
     ("what looked like 1 query was firing 500").
  2. Expressiveness ceiling: chained window functions, DISTINCT ON → Arel internals or raw
     SQL (Rails has no ergonomic window API).
  3. Rot: admin dashboards "fast at launch, degraded after months of production growth."
- **Mitigations users converge on:** denormalized counter columns; cached dashboard
  aggregates; raw SQL/materialized views for hard cases; approximate counts and caps;
  pre-aggregation tables.

### Embedded/customer-facing analytics products

- **Praised:** time-to-market; "feels native" (Luzmo 4.6/5; Embeddable ~4.5/5); warehouse-
  native zero-copy (Mitzu: "I don't have to use reverse ETL tools").
- **Complained:** pricing that punishes exactly the multi-tenant use case (~$70/user/mo
  creators, ~$420/user/yr embedded viewers; ~$60K/yr base + $400/viewer reported; white-label
  gating turning $495/mo into $1,995/mo); open-source BI gates embedding (Metabase AGPL —
  "Metabase embedded is not free"; a Helical Insight 2026 launch open-sourced
  embedding/SSO/RLS/white-labeling explicitly as a wedge against this); theming depth
  ("clunky once you care about UX, permissions, and embedding deeply"); isolation trust —
  on Inconvo's launch a reviewer asked whether tenant scoping inside the AI agent "could be
  tainted by prompt injection"; PostHog shared dashboards have no per-viewer permissions or
  tenant scoping at all.

### Build-vs-buy (HN consensus)

- **"If analytics is core to your product, build. If it's a feature, buy and move on."**
  (avin01; OP: "exactly what I hear 70% of the time.") Both sides have failure stories
  (Tableau "never felt like our product"; an in-house D3 rebuild dropped; an outsourced portal
  cost $4k+ and 8 weeks and "never felt like part of our product").
- **What teams underestimate:** performance at customer scale (Redshift→Snowflake migration,
  scoped queries, capped date ranges); isolation on *every* query path (Omni: "the most common
  multi-tenant security failure" is RLS that works on dashboards but **leaks on drill-downs,
  CSV exports, and scheduled emails**); raw-data demands ("customers were still receiving
  requests for data dumps" — **CSV export is table stakes**); the ownership gap.
- **Architecture successful builders converge on:** thin product-native views over a
  metrics/semantic API; pre-aggregation + low-latency REST over the store (Tinybird model);
  events moved off OLTP into a columnar sidecar. Not iframe BI; not naive on-the-fly ORM
  aggregates.

### Multi-tenant isolation

- **RLS is loved but conditional** (HN 32241820, 254 pts): "makes audits of our tenancy model
  dead simple"; counterpoints — "every index needs to be compound"; views bypass RLS by
  default; session-variable + connection-pooling footguns; CVE-2024-10979 (RLS escape).
- **The canonical leak pattern** (pretix postmortem): "the most dangerous security
  vulnerability in any multi-tenant Django application" is the **forgotten tenant filter** —
  including auto-generated queries; pretix had three vulnerabilities including a critical
  near-leak; their fix (`django-scopes`) is architectural: queries **fail closed** with a
  `ScopeError` until a tenant scope is active. "Most multi tenant systems fail open rather
  than fail closed" (richardw, HN).
- **Per-tenant materialization spectrum in the wild:** shared tenant_id column (most common) →
  schema-per-tenant → DB-per-tenant ("600+ Mongo databases"); enterprise buyers: "multi-tenant
  shared anything is pretty much an absolute dealbreaker... No bank would be allowed."
- **Analytics-specific:** per-tenant predicates interact badly with caching (cache keys must
  fragment per tenant) and non-indexed tenant columns.

### Track 3 sources

- r/django distinct+annotate; forum.djangoproject.com 41803 + 38381 + 26865; SO 17517758 + 61217662; discuss.rubyonrails.org Arel thread; github.com/laravel/framework #18109; SO 74650622; r/laravel Filament rot; woodruff.dev GroupBy; github.com/dotnet/efcore #29593; thinktecture N+1; learn.microsoft.com EF perf; codewithmukesh EF mistakes; LinkedIn 500-queries post; HN 41945458 (Trench)
- G2 Luzmo / Embeddable / Mitzu / Qrvey; cube.dev pricing + embedded; r/dataengineering on Cube; r/BusinessIntelligence embedded-BI costs; posthog.com sharing docs; LinkedIn embedded-analytics cost survey; draxlr + qrvey pricing posts; omni.co embedded implementation guide; querypanel.io RLS roundup; tinybird.co user-facing analytics; HN 46941146 + 44042273 + 44984096 + 36601838 + 49006632
- HN 32241820 (Postgres RLS); HN 41426998 (Fortress); behind.pretix.eu scopes postmortem; cvedetails CVE-2024-10979

## Track 4 — AI-agent analytics trust (2025–2026)

### Text-to-SQL satisfaction reality

- **The benchmark-vs-practice credibility gap:** Spider 1.0 86%+ → Spider 2.0 (enterprise-
  realistic) **21.3%** (o1-preview) / 69.65% (best, Lite variant); LiveSQLBench-Large 30–36%;
  BEAVER warehouse data near **0%** end-to-end. Salesforce's production agent **launched at
  ~50%** efficacy, reaching ~80% only via 10-candidate self-consistency voting.
- **81.2% of errors are schema-level, not syntax** (analysis of 4,602 bad queries) — semantics
  are the problem; schema/metric evolution drops accuracy up to 24 points. Practitioners:
  "high 90s% on a clean database" but **20–30% on messy real schemas**; "90% of the problem is
  data quality"; "The trouble is that it works just enough to be dangerous" (bob1029, HN);
  "Anyone selling you 99% accuracy can prove it there first" (maxdemarzi, re Spider2).
- **Tool sentiment:** Databricks Genie "really struggles with accuracy when you have high
  cardinality string literals... unreliable" + a "have you noticed worse performance lately?"
  regression-anxiety thread; Power BI Copilot the most negative ("people complain endlessly
  that Microsoft keeps pushing Copilot features down our throats"); ThoughtSpot G2 ~4.4/5 with
  an "accuracy cliff" for NLQ without extensive semantic modeling; Vanna.ai recurring
  hallucinated tables/joins, "results with training are worse than asking directly" (#595).
- **Recurring failure modes:** wrong joins (outer joins; MRR computed by joining orders to
  products "skipping the subscription model"); business-acronym misreads (FRT = First Response
  vs First Resolution — "numbers look fine, wrong thing measured"); date/time logic and
  missing filters; non-determinism (same question, different SQL, different numbers); schema
  drift; high-cardinality filters. **Data governance "can make this whole thing a non
  starter"** — the biggest named sales blocker.

### The semantic-layer-as-guardrail pattern — the strongest-evidenced finding

| Source | Without SL | With SL |
|---|---|---|
| dbt Labs 2026 benchmark (Sonnet 4.6) | 90.0% | **98.2%** |
| Same, GPT-5.3 Codex | 84.1% | **100.0%** |
| dbt, in-scope questions only | 51–62.5% | **100%** |
| Cube paired benchmark (3 frontier models) | 45.5–50.5% | 67.7–68.7% (**+17–23pp**, p≤0.0015) |
| Snowflake internal (GPT-4o) | 51% | **90%+** |
| AtScale (TPC-DS high-complexity) | 0% | 70–92.5% |
| Cross-vendor survey (enterprise schemas) | ~40% | 85–95% |

- **dbt's decisive qualitative finding:** "Semantic-layer failure = an explicit error message
  ('it tells you it can't answer'). Text-to-SQL failure = a plausible but silently wrong
  number." Out-of-scope questions score 0% on the SL *by design, and visibly*. The catalog
  **is** the coverage boundary.
- **Cube's striking minimalism:** a **4KB markdown document** of measure definitions and
  disambiguation rules delivered most of the gain and made all three models statistically
  indistinguishable — "the data model is the upper bound on AI analytics"; model choice barely
  matters.
- Practitioner framing: "the text-to-SQL is not the hard part, it's the trust in the data,
  queries, and establishing context using metadata and business jargon" (r/dataengineering);
  Polar Analytics scores ecommerce BI tools on one question: "does the AI query a governed
  semantic layer or guess SQL?"
- **Costs to be honest about:** hand-authored semantic models (Snowflake ~1MB cap; weeks-to-
  months reverse-engineering for large estates); dbt SL's Cloud paywall; catalog latency
  (dbt's 4.8s P95 wall); dbt's own recommendation is a router — semantic layer for accuracy-
  critical use, text-to-SQL as the last fallback.

### Delights (where it stuck)

- **Webmotors + Databricks Genie:** 72% YoY reduction in manual analysis tickets; 200
  analyst-hours/month reclaimed; 100+ monthly active users within six months; 2,800+
  conversations — built on pre-existing Unity Catalog governance.
- **Scheduled agentified answers:** a recurring analysis cut from **90 minutes to 3** — the
  delight came from converting a chat answer into a scheduled, owned artifact.
- **Pinterest:** acceptance of generated SQL rose **20% → >40%** after adding table/column
  metadata plus **verified/reusable queries** to context; their follow-up "Analytics Agent"
  explicitly steers analysts to verified queries rather than free-form generation.
- **Analyst-assist is the reliable delight:** "analysts know SQL but just want a first draft…
  and can eyeball the SQL" — satisfaction is highest when the user can verify the output.

### Trust erosion and abandonment

- **Silent plausible-but-wrong numbers are the trust-killer, not errors** (Readyset's
  documented examples: missing `is_internal` filter inflating MAU 15–20%; `unit_price` picked
  over `price_paid` because "the LLM picked the more obviously named column"). "Most
  LLM-generated SQL doesn't fail. It runs." Business users "treat LLM output as canon and
  share it in meetings."
- **Metric drift destroys meetings** — "queries return different numbers for the same metric."
- **Gartner: ~50% of GenAI projects abandoned after PoC** (poor data quality, inadequate risk
  controls, cost, unclear value); 40%+ of agentic AI projects predicted canceled by 2027.
- **Silent regressions:** unversioned model upgrades quietly degrade working deployments;
  kept deployments pin versions and run **golden-question benchmarks** (known-correct answers,
  run continuously).
- **Permission and audit gaps:** RBAC applied at execution but not intent compilation;
  agent queries indistinguishable from manual queries in audit logs; prompt-injection research
  (0.44% poisoned rows → 79% injection success) cited as a reason not to run arbitrary
  generated SQL.

### What separated kept from killed deployments

1. Answers flow **only from declared, governed definitions** — semantic layer first, raw SQL
   never or last; failures are loud refusals, not wrong numbers.
2. The catalog's **coverage managed as a product** (synonyms, verified queries, sample
   values); teams that stopped feeding it saw accuracy "quietly fall."
3. **Golden-question benchmarks as product infrastructure.**
4. **Verification made visible** — show the named metric/recipe, not raw SQL; deterministic
   answers.
5. **Permissions enforced in the data plane for every entry point** + audit logs that
   distinguish agent queries.
6. **Staged trust** — analyst-assist first; unassisted executive self-serve only after
   measured accuracy on the org's own question set.

### Direct read on the named-recipes bet

The evidence supports it — constrained vocabulary beats open text-to-SQL on every measured
axis (+17pp to +50pp; failures become explicit refusals; determinism becomes architectural) —
with two conditions practitioners hammer on: **coverage must be managed as a product**
(declined questions routed to a visible "request a recipe" loop, or users churn back to
spreadsheets), and **the module should ship its own golden-question harness** with provenance
per answer — which is exactly what kept deployments did and killed ones skipped. Failure-mode
asymmetry worth designing around: hallucinated columns/tables are loud and catchable (>90% of
SQL hallucinations); **wrong joins, missing filters, and wrong metric definitions are silent**
— a named-question catalog eliminates the second, worst class *by construction*.

### Track 4 sources

- docs.getdbt.com "Semantic Layer vs Text-to-SQL: 2026 Benchmark"; cube.dev paired-benchmark post + github.com/cubedevinc/semantic-layer-benchmark; omni.co why-text-to-sql-fails; readyset.io why-LLMs-write-incorrect-SQL; colrows.com Cortex-Analyst alternatives; snowflake.com Cortex-Analyst accuracy; atlan.com Cortex vs T2SQL; datalakehousehub.com semantic-layers text-to-sql; cloud.google.com T2SQL techniques
- HN 45733525 (text-to-SQL is dead); HN 49013995 + cacm.acm.org real-world T2SQL; HN 40456236 (Dataherald); r/dataengineering T2SQL threads (3); r/databricks Genie threads (2); r/PowerBI Copilot threads (2); pub.towardsai.net Copilot 30-day test; r/LangChain T2SQL comparison; github.com/vanna-ai/vanna #556/#595/#440; G2 ThoughtSpot; levelup.gitconnected.com Genie production reality check; polaranalytics.com semantic-layer post
- databricks.com Webmotors case study; community.databricks.com 90-min-to-3; medium.com Pinterest engineering (2 posts); linkedin.com MCP-agents-vs-T2SQL; idinsight.github.io "Using Agents to Not Use Agents"; gartner.com GenAI-abandonment releases (2024 + follow-up)

## Synthesis — mapped against the Koan Analytics module design

### Validated delights (evidence-backed)

1. Define once, never argue again — named recipes are this pattern, verbatim.
2. Metrics-as-code under Git — entity-attached C# recipes go further: definitions live in the
   same file as the entity, versioned with the feature.
3. Serving answers through APIs — Run / tabular Query / REST doors over one governed contract.
4. Sub-second answers at scale — the materialization posture buys this.
5. Verifiable output — the answer envelope (recipe, engine, age) makes verification structural.

### The strategic finding

Standalone semantic layers die economically — "horizontal layers are hard to sell without
owning the application" (Stancil). **Koan owns the application**: an analytics module embedded
in the framework the app already runs on is the one distribution the category's graveyard says
works. The #1 complaint (DSL tax) is answered by the grammar being C# itself, authored where
the entity lives, by the team shipping the feature — also the only credible answer to the
"definition author bottleneck" complaint. The freshness debate landed on declared-freshness
("the only thing you care about is the latency target" — Fivetran) — exactly the module's
`ServeWithin` semantic. And the agent research produced the strongest single number: semantic-
layer-guarded answering at 98–100% on covered questions, with loud refusals replacing silent
wrong numbers.

### Design commitments the research adds

1. **Golden-question conformance harness as a first-class feature** — recipes can carry
   expected-result specs; the catalog runs them on refresh (maps to Koan's capability-tokens-
   co-defined-with-conformance-checks pattern). Kept deployments ran benchmarks; killed ones
   skipped them.
2. **A "request a recipe" loop** — out-of-scope questions fail loudly *and record the gap*;
   coverage is managed as a product.
3. **Refresh-state visibility from day one** — last run, duration, skip reasons in facts.
   Every system grew these dashboards after the pain; ship them first.
4. **Isolation on every path** — exports, drill-downs, and agent/MCP doors get the same
   fail-closed tenant scoping as queries (Omni's leak-point list; pretix's fail-closed rule).
5. **Determinism as a contract** — same question → same answer (recipes give this by
   construction).
6. **Bounded fluid analytics** — scan caps/timeouts on un-promoted aggregates (the 2s→3min
   Django pathologies), with promotion to a materialized projection as the escape from bounds.
7. **CSV/Parquet export as table stakes** — customers demand raw data regardless of endpoints.
