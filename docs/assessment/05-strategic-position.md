# Stage 5 — Strategic position

**Date**: 2026-06-10 · **Status**: strategic framing derived from the assessment (Stages 0–4)
and reviewed with the architect. Decisions recorded in §6 are canon; the theses in §3–§5 are the
working strategy the consolidation tracks should serve.

## §1 The reframed mission

Koan is **not** a commercial product. It is the equivalent of Ruby on Rails for **agentic,
data-driven .NET web applications**: solid architectural foundations, plus enough automation that
a small senior team (and its coding agents) ships sophisticated systems without burning time on
scaffolding. Every keep/cut/invest decision below is evaluated against that mission — not against
market positioning, enterprise sales, or feature parity with anything.

## §2 What the assessment says the real strengths are

1. **Entity-as-universal-grammar — the Rails move, stretched further than ActiveRecord ever
   went.** Rails' center of gravity was AR; everything hung off it. Koan's `Entity<T>` is the
   same center of gravity, but the *same grammar* already covers CRUD, vector search, jobs
   (`IKoanJob` — jobs are entities), caching (`[Cacheable]`), embeddings (`[Embedding]`), media,
   and agent tools (`[McpEntity]`). One mental model radiating across pillars is the rarest
   property a framework can have, and it is the thing Stages 1–3 found genuinely settled. **This
   — not any individual pillar — is the asset.**
2. **Capability-graded honesty as a signature idea.** Every multi-provider abstraction in
   history has lied via lowest-common-denominator. The declare→negotiate→fail-loud model
   (ARCH-0084, DATA-0097) — TTL where native, CAS where supported, pushdown-or-error — is the
   only multi-provider story a senior engineer will believe. Elevate it from internal mechanism
   to stated philosophy.
3. **Reference = Intent on source-generated discovery.** Convention-over-configuration executed
   with build-time machinery: AOT-friendly, deterministic, ordered — not runtime reflection
   magic. This answers the standard enterprise objection to auto-registration, and no public doc
   currently says so.
4. **The self-describing runtime.** Boot provenance, capability reports, `/.well-known`
   surfaces, MCP exposure. A Koan app can *tell you what it is*. Most frameworks can't; this is
   about to become much more valuable than it looks (§3).
5. **The discipline artifacts themselves.** ADR corpus, convergence oracles,
   integration-tests-as-canon, the green ratchet. For a non-commercial foundations project,
   "architecture you can audit" is part of the product: a team adopting Koan inherits not just
   code but a working model of how to make decisions.

## §3 The strategic thesis: the first agent-native application framework

Rails optimized for *developer-minutes* in 2005. The scarce resource in 2026 is **agent-loop
iterations**: most application code is now written with or by coding agents, and frameworks are
increasingly consumed by agents rather than read by humans. Almost everything that makes a
framework agent-friendly, Koan already half-has — mostly by accident. The strategic move is to
make agent-operability a *first-class design goal*:

| Agent-native property | Where Koan stands | What serves it |
|---|---|---|
| **One canonical way per intent** — agents amplify ambiguity; the 18 duplicate-concept clusters are noise injected into every agent-generated diff | Settled core has it; legacy strata don't | Track E (04) is strategic, not janitorial |
| **Fail-loud everywhere** — an agent fixes a loud error in one iteration; a silent swallow breaks its feedback loop entirely | Canon at query time; absent at boot (243 silent catches) | Track F is the single biggest agent-enablement fix |
| **Machine-readable framework knowledge as a shipped deliverable** | `.claude/skills/` exists; partially stale | Keep skills compilable and current; add the missing jobs skill; treat skills as release artifacts |
| **Runtime introspection over MCP** — the agent doesn't read docs, it *asks the running app*: "your entities, adapters, capabilities, jobs, boot report" as MCP resources | `[McpEntity]` + boot provenance + capability reports exist; not yet joined | One small dev-mode package away; no mainstream framework does this |
| **Scaffold-by-agent replaces `rails generate`** | Skills catalog is half of this | Curate canonical-pattern skills; don't build a templating engine |

**The wedge demo**: Rails' was "build a blog in 15 minutes" (a screencast). Koan's should be
**"an agent builds a working, observable, multi-provider AI app in one session — and the
framework keeps it honest"** (an agent transcript). Its prerequisites are exactly Tracks A
(installable packages), E (one way), and F (loud failures) — a useful sanity check that the
consolidation plan and the strategy point the same direction.

## §4 The second wedge: own the data→AI seam in .NET

Every .NET shop adding AI hand-rolls the same plumbing: chunking, embedding generation and sync,
vector store wiring, RAG over domain data, model routing. Koan's
`[Embedding]` → background worker → vector sync → `SemanticSearch` seam is dogfooded and real.
**The flagship AI story is "AI as a property of your entities"** — not the speculative lifecycle
verticals (§5). Adjacent and compatible: the local-first/sovereign stack (Ollama + Postgres +
Weaviate on one box, scaling up the capability ladder from in-memory to distributed) serves
precisely the small-senior-team-plus-agents audience the mission names.

## §5 What to shed — strategically, not just by consumer-count

These extend the Track C dispositions (04) with the *why* at mission level:

1. **The MLOps ambition** (Training, Eval, Compute, most of Models). These serve people who
   *operate* models; Koan's lane is apps that *consume* models against domain data. A quarter of
   the project count dilutes the AI story that is actually distinctive. The external orchestrator
   can own lifecycle; keep model-pull only as far as app bootstrapping needs it (Ollama).
2. **Orchestration as a product.** Already decided (ARCH-0077); the strategic framing: Koan wins
   by being the best *Aspire citizen*, not by owning compose generation. Execute before it rots
   further (Track D).
3. **The enterprise-governance persona.** Pilot KPIs and champion guilds serve nobody here. The
   audience is the small senior team and its agents; keep the operational seriousness (boot
   reports, health, trust), drop the theater.
4. **GraphQL and WebSockets.** Beyond zero consumers: in an agent-consumed world the consumption
   surfaces that matter are REST+OpenAPI, SSE, and MCP. GraphQL's "flexible client queries" use
   case is being eaten by agents calling capability-described REST. Off-thesis, not just unused.
5. **Second-framework surfaces** (Cqrs vs Jobs ledger, Recipe vs KoanModule, ServiceMesh vs
   Aspire/ZenGarden discovery, Scheduling vs Jobs). Each is a parallel answer to a question
   already answered better. **Reference = Intent only works if each reference maps to exactly
   one intent.**
6. ~~Serializer ambiguity → pick STJ~~ — **superseded by the recorded decision in §6.1.** What
   *is* shed is the ambiguity itself: the parallel STJ island, not Newtonsoft.

One honest caveat that stays open: **ambient context** (AsyncLocal `EntityContext`,
`AppHost.Current`) is load-bearing for the ergonomics *and* the riskiest design surface left —
Stage 2 found a doc/code contradiction in its scoping semantics, and ambient state is where both
humans and agents get bitten silently. Facet 3 is correctly sequenced as the hard one; sharp,
documented, test-pinned ambient semantics are a precondition for calling the foundation settled.

## §6 Recorded decisions (architect-reviewed)

### §6.1 Serialization: Newtonsoft.Json is canonical

**Decision**: Newtonsoft.Json stays as the framework's serializer. Rationale: its handling of
polymorphic object graphs is less surprising in practice than System.Text.Json's — for a
framework whose entities flow through documents stores, transformers, messaging envelopes, and
agent-facing JSON surfaces, polymorphism ergonomics outweigh STJ's performance posture. If
anything, STJ is the surprise generator in this problem space.

Consequences:
- The "two serializer worlds" finding (02 §6) resolves by **removing the STJ island**
  (`Koan.Web.Json.Strict` cut/absorb per Track C), not by migrating the pillar.
- **Document the Newtonsoft defaults loudly** (camelCase, ignore-nulls) in getting-started and
  the api-building skill — the Stage 2 newcomer-walk flagged the serializer surprise precisely
  because it is *undocumented*, not because it is wrong.
- **Known tension to manage, not relitigate**: Newtonsoft is reflection-based, which sits
  uneasily with the NativeAOT/source-gen direction (the g1c1 sample dogfoods AOT publish).
  Treat AOT+Newtonsoft compatibility as an engineering constraint to verify per pillar (the
  internal STJ source-gen contexts used by adapters for their own wire formats, e.g. Redis cache
  envelopes, are fine and stay — they are not the app-facing serializer).

### §6.2 Theses accepted as working strategy

The agent-native thesis (§3) and the MLOps shed (§5.1) were presented for challenge and stand as
the working strategy. They should be revisited if the mission statement changes.

## §7 Operating playbook: agentic sessions with lesser models

The consolidation backlog is large, and much of it is mechanical — ideal for cheaper/smaller
models *if* the repo is set up so they cannot wander. These rules are written to be pasted into
session prompts or referenced from CLAUDE.md.

### §7.1 Route tasks by tier

| Tier | Task classes | Examples from the assessment backlog |
|---|---|---|
| **Small model, autonomous** | Delete-only, regeneration, status sweeps — verifiable by build alone | Wave 0 debris ledger (01 §5); dead `InternalsVisibleTo`; `.lscache` cleanup; ADR status banners (Track A.5); CATALOG.md regen from sln; README link/badge fixes |
| **Small model, recipe-driven** | Mechanical migrations with a written, verified recipe and a test that pins behavior | XRegistration→registrar folds (Track E.1); Scheduling cut (full recipe in evidence/stage4/scheduling-cut.json); copy-pasted enum hoists; doc-comment fixes |
| **Frontier model only** | Design-needed, hot-path, or semantics-bearing work | Ambient-context semantics (Facet 3); capability-model changes; adapter pushdown logic; auth flow engine swap; ES/OS dialect-seam extraction; anything touching `Entity<T>` public surface |

The migration-cost tags already in the consistency audit (delete-only / mechanical /
design-needed, evidence/stage2/consistency.json) are the routing key — keep tagging future
backlog items the same way.

### §7.2 Session protocol (any model, mandatory for lesser ones)

1. **Start from evidence, not exploration.** Point the session at the specific pillar's
   evidence JSON (`docs/assessment/evidence/pillar-*.json`) + the governing ADR + the one
   target area. Lesser models burn their budget (and reliability) on open-ended repo discovery;
   the assessment exists so they don't have to.
2. **One intent per session.** One cut, one fold, one sweep. The green ratchet
   (`scripts/green-ratchet.ps1`) runs before and after; a session that can't get back to green
   reverts, it does not "fix forward".
3. **Never invent APIs — grep first.** The repo's own docs hallucinate APIs (Stage 2), so
   doc-trained intuitions are *worse than nothing* here. Rule: any API referenced in generated
   code must be evidenced by a `file:line` the session actually read. This mirrors the
   project's standing rule that agent findings are naive until re-derived.
4. **Outputs carry evidence.** Claims in summaries/commits cite `file:line`. "Removed X, which
   had zero references (verified: grep pattern, 0 hits)" — not "cleaned up unused code".
5. **No-go zones for lesser models** (explicit, because they will not infer them): ambient
   context internals, `RegistrySourceGenerator`, capability token semantics, adapter
   query-translation paths, anything under `src/Koan.Data.Core/Model/`, public API renames.
6. **Stop conditions.** A lesser-model session that encounters: a failing test it didn't
   expect, an API that doesn't match the recipe, or a second plausible way to do the task —
   stops and reports rather than choosing. Choosing is the frontier model's job.

### §7.3 Repo investments that raise the lesser-model ceiling

Each of these converts reviewer vigilance into build-time enforcement — the best "reviewer" for
a weak model is the compiler:

1. **Canonical-pattern cards.** One short, *compiling* exemplar per intent: entity, controller,
   cached entity, embedding entity, job, KoanModule, adapter skeleton. Promote
   `docs/examples/_canonical-samples.md` from `status: draft` to normative, extend it, and make
   the doc-example compilation lint (already in the ratchet) cover it. Lesser models copy
   patterns; give them exactly one copy-source.
2. **Analyzers over conventions.** Generalize the KOAN0001 pattern into `Koan.Analyzers`
   (Track E): registration pitfalls, manual-DI-of-framework-services, partition-string-overload
   use in new code, `catch {}` on boot paths. Every rule an analyzer enforces is a rule a small
   model cannot silently break.
3. **Keep skills compilable.** The debugging skill's canonical snippet currently doesn't compile
   (Stage 2) — for a lesser model that's poisoned ground truth. Skills join the doc-lint gate;
   add the missing koan-jobs skill.
4. **Fail-loud boot first** (Track F). A lesser model relies entirely on the error channel; the
   ~20-line AppBootstrapper fix is the cheapest single improvement to *every* future agentic
   session's success rate.
5. **CLAUDE.md anti-pattern list stays current.** It is the highest-leverage context artifact
   for small models; the Stage 1–2 findings (no manual framework-service registration, no new
   XRegistration classes, no new `Add*` methods where a registrar exists, Newtonsoft is the
   serializer) belong there as one-liners.

## §8 The premium-DX program: documentation, presentation, narrative

Goal: **premium DX, minimal cognitive load, no barrier of entry.** The assessment showed the
inverse today (onboarding is inverted — the deeper you go, the better it gets; 02 §3). This
program turns that around. It expands Track H (04) from mechanics into a full
information-architecture and narrative redesign.

### §8.1 One narrative spine: "your entities are the app"

Today the story is told four ways to four personas, and the loudest telling is false. Replace it
with **one story in three beats**, told identically everywhere (README, getting-started, skills,
talks):

1. **Model an entity → get an application.** `Entity<T>` + `EntityController<T>` + the 4-line
   Program.cs = a full REST API with health, logging, IDs, schema. (Verified true — this is the
   framework's best moment; lead with it.)
2. **Reference an intent → gain a capability.** Add a package: storage swaps
   (Sqlite→Postgres→Mongo), `[Cacheable]` caches, `IKoanJob` schedules, `[Embedding]` makes it
   semantically searchable, `[McpEntity]` hands it to agents. Each beat is one package + one
   attribute — that *is* Reference = Intent, shown not told.
3. **The app explains itself.** Boot report, capability negotiation, `/.well-known`, MCP
   introspection. Close on this — it is the differentiator no other framework has, and it is
   currently sold nowhere.

Tagline accordingly — concrete and verifiable, not aspirational. Working draft:
*"Model your domain as entities. Reference your intents. Koan composes the rest — storage, web,
AI, jobs, caching — and tells you exactly what it did."* Retire "intelligent automation /
elegant scaling" vocabulary; every adjective in the front door must be checkable.

### §8.2 Information architecture: four reader surfaces, strictly

Collapse the 27 docs directories into a Diátaxis-style purpose taxonomy. A page lives in exactly
one surface; every page states its surface and links up/down:

| Surface | Reader question | Today's sources to fold in |
|---|---|---|
| **Learn** (`getting-started/`) | "Take me from zero" — the golden path, 15-minute tour, concept budget | overview, quickstart tombstones, examples/ |
| **Guides** (`guides/`) | "How do I do X" — one guide per intent, each ending "see it live in S\<n\>" | how-to/, patterns/, workbooks' task content |
| **Reference** (`reference/`) | "What exactly is the surface" — generated API docs, capability matrix per adapter, options catalog, boot-report format | api/, the missing adapter×capability matrix (auto-generate from `Describe()`) |
| **Decisions** (`decisions/`) | "Why is it this way" — the ADR corpus as a first-class product: *architecture you can audit* | design/, specifications/, proposals (graduated or archived) |

Everything else → `archive/` (including prior-art/, the migration one-shots, session logs) or
`engineering/` (contributor process, merged with workbooks). The assessment lives at
`docs/assessment/` as the self-audit surface — link it from the front door as a trust signal,
not a skeleton in the closet: pre-1.0 honesty is a feature.

### §8.3 Snippet truth as a build guarantee

The root cause of the 25-FALSE problem is snippets written by hand and never executed. The fix
is mechanical and the machinery half-exists (`validate-code-examples.ps1`):

- **Single snippet source-of-truth**: promote `_canonical-samples.md` to a compiled snippet
  library; README, guides, and skills *embed* from it (or are covered by the same lint).
- **The stated guarantee — "if it's in the docs, it compiles"** — becomes a CI gate (Track B)
  covering 100% of user-facing code blocks, not just diff-scoped instructional docs.
- **Version stamps are injected, never typed**: NBGV writes the version into doc front-matter at
  build; the frozen-v0.6.3 class of drift becomes impossible. "Validated" stamps may only be
  emitted by the lint that actually validated.

### §8.4 The first ten minutes: kill the barrier, don't document around it

- **`dotnet new koan-web` (and `koan-console`) templates.** Rails' real onboarding was never
  docs; it was `rails new`. A template eliminates, in one stroke: the `Sylin.*` package-ID
  confusion, the multi-package `dotnet add` failure, the seven bootstrap variants (template
  ships the canonical 4-line Program.cs), and the serializer surprise (template's README block
  prints the camelCase/ignore-nulls defaults per §6.1). This is the single highest-leverage
  barrier-of-entry artifact and should precede any README rewrite that still teaches package-by-
  package assembly.
- **README first screen ≤ 60 seconds**: one working command block (template), one entity, one
  `dotnet run`, then a *real boot-report capture* — the boot report is the demo. No feature
  matrix above the fold.
- **An honest status banner**: pre-1.0, NBGV version, "what's settled vs experimental" linking
  the maturity model (03). Senior evaluators — the actual audience — trust a framework that
  grades itself far more than one that claims "production-ready" in a badge.
- **The clone path documented**: "clone → open Koan.sln → run S0" exists in the repo today as
  the only working path and is written down nowhere. Two paragraphs fix it.

### §8.5 Concept-budget discipline (minimal cognitive load, made explicit)

- **Publish the budget.** Hello-CRUD = 8 concepts (02 §3 names them). Every pillar guide opens
  with "New concepts introduced: …" and a running total. What is measured stays small; this also
  gives consolidation a user-facing metric (concept count per scenario, tracked in 04 §9).
- **One verb set.** The four naming dialects (Save↔Upsert, Remove↔Delete, Query↔Where) get one
  canonical vocabulary; synonyms remain as `[Obsolete]` aliases for a release, then go. Docs use
  only the canonical verbs from day one.
- **Docs never show the second way.** One canonical way per intent in all teaching material;
  alternatives appear only inside an explicit "Escape hatch" callout box. (The framework may
  layer; the *documentation* must not.)
- **One page per pillar** ("the map card"): what it does, the one canonical pattern, the ≤5
  attributes you'll actually use, the escape hatch, the sample that shows it live. One screen,
  generated structure, kept under the snippet lint.
- **A glossary.** Partition, set, source, adapter, capability, provenance, lane, gate — Koan's
  proprietary vocabulary is its real cognitive tax (02 §3); define each term once, link
  everywhere.

### §8.6 Voice and presentation rules

- **One voice: plain, confident, evidence-citing.** Ban self-stamps ("production-ready",
  "enterprise-grade") — status language comes only from the maturity model. Strip emoji walls
  and week-numbered phase plans from anything user-facing.
- **Show real output, not promises**: boot-report captures, an actual fail-loud error with its
  actionable message, an agent transcript building a feature. The framework's character is
  *self-describing honesty* — the presentation should perform it.
- **The comparison table is capability-graded** like the framework itself: every 🟩 must cite a
  shipped, tested feature (the current table rests partly on the deleted Flow pillar). An honest
  🟨 with a roadmap link beats a false 🟩 with every audience that matters.
- **Errors are documentation entry points.** The resolution-failure messages already name exact
  config keys (praised in 02 §4); standardize: every framework-thrown error names the fix or the
  guide anchor. For agent sessions this is *the* primary doc channel.

### §8.7 Agent-facing presentation (the §3 thesis, applied to docs)

- **`llms.txt` + condensed canon** at repo root: the three-beat story, the canonical patterns,
  the anti-pattern list, the verb vocabulary — sized for a context window, generated from the
  same snippet source-of-truth.
- **Skills are release artifacts**: versioned with the framework, covered by the snippet lint
  (the debugging skill currently teaches a non-compiling signature), one skill per pillar
  including the missing koan-jobs.
- **Dev-mode MCP introspection** (§3): the running app as its own documentation — entities,
  adapters, negotiated capabilities, boot report as MCP resources. Propose via ADR; it converts
  the boot-report investment into the agent-native differentiator.

### §8.8 Samples as the curriculum

Renumber so **ladder order = reading order**, regenerate the catalog from the sln (Track A), and
fill the missing middle rungs (messaging, jobs, cache as first-class teachable samples — today
they exist only embedded in larger apps). Each guide ends "see it live in S\<n\>"; each sample
README opens "this chapter teaches: …" with its concept-budget delta. Flagships (S5/S7/S18) are
explicitly labeled *dogfood apps, not tutorials* — aspiration and curriculum stop being mixed.

### §8.9 Sequencing & ownership

Order of operations (mostly small-model-executable under §7 once the recipes are written):
(1) template + README first-screen rewrite [frontier model], (2) snippet lint to 100% + version
injection [recipe], (3) IA collapse 27→5 dirs [recipe], (4) pillar map cards + glossary
[frontier drafts, small models maintain], (5) verb-set decision + alias sweep [frontier decides,
small models execute], (6) llms.txt + skills refresh [recipe], (7) MCP introspection ADR
[frontier]. Items 1–3 alone remove the "no barrier of entry" blockers; 4–5 deliver the
cognitive-load floor; 6–7 are the premium layer.

## §9 How this maps onto the tracks (04)

| Strategic item | Track |
|---|---|
| Agent-native: one canonical way | E (+ C cut waves remove competing ways wholesale) |
| Agent-native: fail-loud loop | F |
| Agent-native: shipped knowledge + MCP introspection | H (skills) + a small new dev-mode MCP package (propose via ADR) |
| Data→AI seam as flagship | A (README rewrite sells it), C (AI vertical collapse clears the noise around it) |
| Aspire-citizen posture | D |
| Serializer decision (§6.1) | C (cut the STJ island), H (document defaults) |
| Lesser-model playbook | §7 here; reference from CLAUDE.md |
| Premium-DX program (§8) | Expands Track H; templates + snippet lint ride Track B's CI; README/IA work rides Track A; sequencing in §8.9 |
