# Stage 2 — Philosophy & developer experience

**Date**: 2026-06-10 · **Method**: 6 parallel audits — canon extraction, claim-by-claim promise
verification, three-path newcomer walk, ergonomics/error-experience review, public-surface census,
"how many ways" consistency audit. Raw findings: [evidence/stage2/](evidence/stage2/).

---

## 1. The canon, in two eras

The framework has **two distinct philosophy layers**, and they don't fully agree.

**Founding promises** (principles.md, README, overview — 2025 era):
Reference = Intent · Entity-first ("no repository pattern") · provider transparency ("storage is a
deployment concern") · simplicity-first (KISS/YAGNI) · deterministic config, **fail fast** ·
progressive complexity · escape hatches everywhere · container-native · self-reporting boot ·
AI-native, not bolt-on · "small teams, sophisticated apps".

**Consolidation-era principles** (foundation-consolidation-plan, ARCH-0084/0086 — 2026):
"Fewer but more meaningful parts — descaffold, don't lovingly refactor" · count developer-facing
*intents*, not projects · dogfood-driven collapse (≥2 real usages) + green ratchet · unified
capability model as the sole surface, **fail-loud is canon** · KoanModule built over working
machinery, never big-bang · premium-DX audience for internals = the adapter author · integration
tests as canon (ARCH-0079).

The consolidation-era canon is the healthier one — it is empirical, self-correcting (ARCH-0086
publicly reverses its own plan's premise on evidence), and matches how the best recent code was
actually built. The founding canon survives mostly as **unmaintained marketing**: principles.md is
the document the README calls the "Enterprise Architecture Guide", and it is the least accurate
document in the repo (§2).

## 2. Promise audit: the front door fails verification

59 distinct concrete claims/snippets in README + principles.md + getting-started were verified
against `src/`:

> **TRUE: 22 · PARTIAL: 12 · FALSE: 25.** By source: README 9/6/6, overview 5/3/6,
> **principles.md 10/5/13 — the worst, despite a "validation verified 2026-03-25" front-matter
> stamp.** The validation stamps themselves are therefore false.

The five most damaging drifts for a newcomer (full table in evidence):

1. **The front door is locked.** `dotnet add package Koan.Core Koan.Web Koan.Data.Connector.Sqlite`
   fails three ways: real IDs are `Sylin.Koan.*` (Directory.Build.props:31), nuget.org holds
   accidental unprefixed 0.5.2 relics + stale `Sylin.*` 0.8.0 vs repo 0.17, and no user-facing doc
   ever mentions the `Sylin.` prefix. Worse than a 404: `Koan.Core`/`Koan.Web` **silently install
   a 13-month-stale 0.5.2**.
2. **The Flow pillar is a ghost.** `Flow.OnUpdate<T>` + `UpdateResult.Continue()` is showcased in
   README §4, overview Stage 3, *and* enterprise-adoption's compliance example — zero matches in
   any `.cs` file; `src/Koan.Flow.Core/` holds one orphaned TECHNICAL.md. The comparison table's
   "Event-driven & projections 🟩" rests on it.
3. **Both onramp repos are 404s.** `github.com/koan-framework/{quickstart,enterprise-sample}` —
   the org itself doesn't exist (verified via GitHub API).
4. **Every controller-customization sample fails to compile.** Documented `override Post/Put`
   don't exist; the real override point is `virtual Task<IActionResult> Upsert(...)` with no PUT
   verb (EntityController.cs:403).
5. **The entity API in principles.md is systematically wrong**: `Todo.Where`, `todo.Delete()`,
   `Query().Where().OrderBy().Take()`, `SaveBatch`, `ExecuteSql`, `KoanException`,
   `KoanEnv.Features`, `AddKoan(options)` — 8 core snippets don't exist (real: `Query(predicate)`,
   `Remove()`, `UpsertMany`, `Data<T,K>.Execute<TResult>(sql)`).

## 3. Onboarding is inverted: the deeper you go, the better it gets

- **Outermost ring (README, nuget.org): actively hostile.** Every public install command is wrong;
  the badge/docs/skills pin v0.6.3 — a version never published; the quickstart skill even pins
  `--version 0.6.3`, which cannot resolve. Probability an outsider reaches hello-CRUD from the
  README alone: near zero.
- **Middle ring (getting-started, samples catalog): 8 months stale misdirection.** quickstart.md
  and guide.md are deprecation tombstones redirecting to overview.md, which re-teaches the broken
  commands plus the ghost Flow API. CATALOG.md advertises four phantom samples ("In Development,
  Phase N, Weeks X–Y") as the *Best Example* cells for the entire Messaging, Secrets, Media, and
  CQRS rows, while hiding five real samples. **There is no documented "clone the repo, open
  Koan.sln, run a sample" sequence anywhere — the only path that works is undocumented.**
- **Innermost ring: genuinely good.** The S0→S1→S10→S14 ladder is real, runnable, CI-protected,
  and impressively low-ceremony. The canonical 4-line Program.cs
  (`builder.Services.AddKoan(); app.Run();`) **is functionally sufficient** — verified:
  KoanWebStartupFilter auto-wires controllers, health, static files, secure headers, AppHost.
  But it is stated authoritatively only in a `status: draft` doc and an AI-skill file, **no
  shipping sample uses it**, and 8 of 13 in-sln samples carry redundant
  `AppHost.Current ??= app.Services` / `AsWebApi()` incantations that re-assert defaults —
  teaching newcomers that the magic needs manual safety nets. Seven bootstrap variants exist
  across samples/docs; the flagship S5.Recs manually registers a framework service in violation
  of the bootstrap skill's own "never do this" rule.

**Concept economics** (newcomer-walk): hello-CRUD needs ~8 Koan concepts — about equal to
ASP.NET+EF in *count* but far fewer lines; an S5-class app needs ~24. The catch: 100% of those
concepts are proprietary, documented only by this repo — and the repo's entry-level documentation
is its least trustworthy layer. For the AI/vector/polyglot app class, the integration savings are
genuinely large (plausibly thousands of lines).

## 4. Ergonomics & the failure experience

| Dimension | Score (1–5) | Summary |
|---|---|---|
| API ergonomics | **3.5** | Happy path delivers the AR-without-traps promise; edges ship real traps |
| Failure experience | **2** | Fail-fast is not real at boot; fail-at-first-use with raw provider exceptions |
| Boot report | **3.5** | Real and above-average; oversold — collected provenance mostly isn't shown |
| Debuggability | **2.5** | Professional SourceLink/logging plumbing; no exception model; drifted debug docs |

**API**: `Todo.Get/Save/Query` with a single execution path and centralized pushdown is genuinely
good architecture; the awaitable `Count` accessor and capability probe-by-cast are clever. But
IntelliSense on `Todo.` shows **~60 statics** (the partition-string overload matrix multiplies
every verb); four naming dialects coexist (Save *is* Upsert, entity `Remove` vs facade `Delete`,
Data's `Query` vs Jobs' `Where`); relationship navigation loads **all rows and filters in
memory** (Entity.cs:843 admits it) — a shipped AR trap; and EntityContext's doc says "replaces,
does not merge" while the code inherits four fields from the previous scope (doc/code
contradiction in one file).

**Failure**: the smoking gun is `AppBootstrapper.cs:110-124` — the `AddKoan()` initializer loop
wraps every module registration in `catch { /* best-effort */ }` with **no logging**. A module
that throws during registration vanishes; the developer meets it later as "Unable to resolve
service". A wrong connection string produces a *healthy-looking start* and a raw `NpgsqlException`
at first use. Config typos silently fall through to defaults (AdapterConnectionResolver). Census:
**243 empty/comment-only catch blocks across 93 files** (plus 136 return/continue-only), ~75-80%
untyped-and-unlogged, concentrated on the boot path and the Sqlite adapter (23 sites in one file).
This directly contradicts both the founding "fail fast" and the consolidation-era "fail-loud is
canon" (ARCH-0084) — the same framework that makes an un-pushable filter a hard error at query
time institutionalizes silence at boot time. Counterweight: when *resolution* fails, messages are
excellent (missing connection string lists the three exact config keys; missing adapter names the
fix).

**Debuggability**: SourceLink/snupkg/deterministic builds are correctly set up. But there is no
`KoanException` hierarchy (~27 scattered one-off exception types; the data core throws generic
`InvalidOperationException` and lets raw driver exceptions escape), two parallel console
formatters coexist, and the troubleshooting docs + debugging skill teach log markers and a
`Describe(BootReport, ...)` signature that no longer exist.

## 5. The surface census: the concept budget is spent upside-down

- **2,351 public types** across src; users realistically live in ~645 of them (Core + Data inner
  ring + Web nucleus + Cache + Jobs + Vector + Trust + 1-2 connectors) — **~27% of the surface**.
- The periphery group alone: 531 types / 49k LOC (26% of all code) with consumption measured in
  single samples. The AI vertical: 311 types + 6 of the framework's 17 root facades + a 96-type
  DTO contracts package for **one sample**.
- **9 of 17 root static facades live in packages with 0–1 consumers** (Model, Compute, Eval,
  Training, Review, Agent, Rag, Translation, ZenGarden).
- **Koan.Core is the single fattest package** (224 public types, 73 static classes, 20 Options)
  and the one mandatory dependency — every user pays the maximum-surface toll on day one.
- Annotation vocabulary: **76 distinct attributes** (4 names defined 2-3× — `KoanServiceAttribute`
  ×3); a realistic app meets ~25-30. Config surface: **170 `*Options` classes** (~145 truly
  bindable) and **77 `Add*` extension methods** coexisting with the promised single `AddKoan()`.
- Counter-evidence that the redesign discipline works: **Cache = 78 types / 7 projects, Jobs = 49
  types / 2 projects** — flagship pillars at ~⅓ the type cost of equivalent legacy strata.
- The condemned Orchestration stack's vocabulary is structurally load-bearing: 37 types + 10 of
  the 76 attributes, referenced by 24 projects. Condemning the CLI did not condemn its annotation
  surface.

## 6. "How many ways": 18 duplicate-concept clusters

The consistency audit found **18 clusters** where the framework offers N>1 ways to do one thing:
**5 deliberate layering** (data-access entry tiers, bootstrap invocation rungs, authorization
seam, eventing by guarantee class, AI composition ladder), **7 drift**, **6 unfinished migration**.
By migration cost: 5 delete-only, 9 mechanical, 4 design-needed.

Notable:

- **Bootstrap authoring**: 90 `IKoanAutoRegistrar` implementations vs 7 `KoanModule` subclasses —
  **7.2% migrated**. This 90:7 ratio is the single best longitudinal consolidation metric.
- **Self-admitted duplication in code**: Postgres KoanAutoRegistrar.cs:51 — "The explicit
  AddPostgresAdapter() registration call already does this" — and the two paths have *already
  diverged* (auto registers more than manual), which is how dual registration becomes a
  correctness bug.
- **Complete supersessions where the old pillar still ships**: Koan.Scheduling vs JOBS-0005
  Schedule (the old one's own README admits cron never shipped); Cqrs outbox vs the Jobs ledger's
  transactional enlistment. Neither is marked obsolete anywhere.
- **Dogfooding inconsistencies that mislead source-readers**: EntityEndpointService injects
  IDataService *and* calls `Data<T,K>` statics in the same class; Koan's own auth built-ins still
  implement the legacy contributor interface their replacement deprecates; the instance
  `entity.SaveVectorAsync()` extension silently requires a config flag the static facade doesn't.
- Verb synonyms (Save↔Upsert, Remove↔Delete) and partition-string overloads are *documented sugar
  over single paths* — the cost is surface width, not behavioral divergence.

## 7. Audience: four personas, one real user

The docs address four personas that pull apart: the README's "be delighted in 2 minutes" indie
dev (blocked at step 1), enterprise-adoption.md's governance apparatus (pilot KPIs, champions
guild — for a framework with zero external consumers), principles.md's "experienced teams willing
to invest in framework-specific expertise" (the opposite onboarding posture from the README), and
the consolidation ADRs' adapter author ("samples never touch these types"). The only persona
actually served today is the undocumented fifth: **the sole implementor-consumer dogfooding toward
v1** — which the consolidation plan states candidly, making it the only document whose stated
audience matches reality.

## 8. Undersold strengths (the inverse drift)

The docs oversell ghosts while underselling what is actually distinctive and settled:

1. **The unified capability model** (ARCH-0084) — one declare/negotiate/report primitive across 14
   adapters with fail-loud semantics. README gives it one generic table row; principles.md still
   shows the deleted generation.
2. **Build-time source-generated discovery** — Roslyn-generated `[ModuleInitializer]` registry
   with topological ordering: AOT-friendly, deterministic, *not* runtime reflection magic. This
   answers the #1 enterprise objection to auto-registration frameworks, and no public doc says it.
3. **The Jobs pillar** (JOBS-0005) — entity-first jobs with a capability ladder under a constant
   at-least-once contract, CAS claim, store-native TTL. modules-overview.md actively misdescribes
   it as the pre-rebuild design.
4. **Transparent entity caching** (ARCH-0075/0078) — [Cacheable] L1/L2 + coherence with a
   principled fresh-or-null contract. Absent from README entirely.
5. **The engineering discipline itself** — green ratchet, cross-adapter convergence oracles that
   found real bugs, evidence-corrected ADRs. For an evaluating architect this correctness culture
   is a stronger trust signal than any feature list, and it is invisible outside docs/decisions/.
6. **The honest multi-provider story** — capability-graded behavior (TTL where native, CAS where
   supported, pushdown-or-fail-loud) is more credible to senior engineers than the absolute "same
   code works everywhere" claim the docs actually make. The framework undersells itself by
   overclaiming.

---

*Next: [03-maturity-model.md](03-maturity-model.md) — placing each pillar on an explicit maturity
ladder.*
