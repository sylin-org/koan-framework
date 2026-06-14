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
| B1 | T2 | — (first) | done | 2026-06-13 | opus-4.8 | (uncommitted) | sln truth — added 25 buildable test projects (168→195 incl. 2 transitive prod refs); EXCLUDED 13 broken/husk projects beyond the card's 5 (see Divergence log) — build green, `--list-tests` enumerates 2922 tests / 71 assemblies |
| B2 | T2 | B1 | pending | | | | CI PR gate |
| B3 | T3 | B1, B2 | pending | | | | NBGV-native release; nuspec metadata fix |
| A1 | T1 | — | done | 2026-06-13 | opus-4.8 | c3a13de8 | 30 ADRs marked; docs-lint 0 errors. Drift: FLOW banner reworded truthfully (Flow→Canon mid-flight, not removed); DATA-0019 skipped (card mislabels Outbox as Cqrs; no CQRS ADR exists) |
| A2 | T2 | — | done | 2026-06-14 | opus-4.8 | 8bcc0bb6 | ghost APIs (Flow/.Embed/koan-CLI/Describe(BootReport)) + 0.6.3 pins removed across 6 docs; docs-lint clean. Ran parallel w/ H5+H7 |
| A3 | T1 | — | done | 2026-06-13 | opus-4.8 | ad13b65c | 11 tracked litter + 103 *.lscache (card said ~9) + stale Cache/Unit; build green; ran parallel w/ A1; fixed 1 cross-card link (DEC-0053) |
| C0 | T1 | — | pending | | | | wave 0: debris/tombstone directories |
| C1 | T2 | B1 | pending | | | | cut Koan.Data.Cqrs (+ Mongo outbox) |
| C2 | T2 | B1 | done | 2026-06-13 | opus-4.8 | ffef0899 | cut Koan.WebSockets (197→195); 0 src consumers, absent from ledgers/nuspec; build + bootstrap integration 17/17 green |
| C3 | T2 | B1 | done | 2026-06-13 | opus-4.8 | 035dc891 | cut Koan.Web.Json.Strict (195→193); STJ island, 0 consumers, absent from ledgers/nuspec; build + bootstrap integration 17/17 green |
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
| F1 | T2 | — (early) | done | 2026-06-14 | opus-4.8 | 5781b18f, aa981cbf | fail-loud boot (KoanBootException + MODULES-FAILED block + KOAN_BOOT_LENIENT); ARCH-0079 specs. **Gate caught a coverage gap** (manifest-invoker + render untested) → remediated (+5 specs + minimal test seam); BOTH mutation checks pass (specs fail when behavior reverted). Bootstrap 24/24 |
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
| H5 | T2 | — | done | 2026-06-14 | opus-4.8 | 6b3894ec | glossary (19 terms, each type-anchored); caught 'set'→'partition' drift + BootReport-not-a-type; fixed pre-existing index.md broken link |
| H6 | T2 | H2 | pending | | | | verb alias sweep (docs only) |
| H7 | T2 | A2 | done | 2026-06-14 | opus-4.8 | f28792f9 | /llms.txt (111 lines), verbatim snippets from overview.md; README pointer; ran parallel w/ A2+H5 |
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

## Cards discovered during pilot execution

New cards surfaced while running other cards (not in the original 06/07 stash). Same columns.

| ID | Tier | Deps | Status | Date | Agent/model | Commits | Notes |
|---|---|---|---|---|---|---|---|
| X-KoanContext | T3 | — | pending | | | | **park / re-home** `src/Services/code-intelligence/Koan.Service.KoanContext` out of the framework repo. 14.8k LOC, net10.0, the repo's largest project, **never in Koan.sln**, zero in-repo consumers; breaks are its own mid-refactor debris (`IndexProject`→`IndexProjectAsync` delegate rename), not framework drift. Canon already recommends eviction (`pillar-periphery-services.json:298`, Zen Garden precedent). Triaged 2026-06-13. Move it + `tests/Suites/Context/**`; keep its docs/ADRs as the trail. |
| X-FluentAssertions | T2 | — | pending | | | | finish the FluentAssertions v6→v7 migration left behind on excluded test projects: `S16.PantryPal.Tests` (`BeGreaterOrEqualTo`→`BeGreaterThanOrEqualTo`, `HaveCountGreaterOrEqualTo`→`HaveCountGreaterThanOrEqualTo`, + a CS1997 async `DisposeAsync`); audit the other excluded husks for the same rot; re-add to sln once green. |
| X-snippet-lint-fix | T2 | — | pending | | | | fix `scripts/validate-code-examples.ps1` — it crashes at line 219 (PowerShell SwitchParameter bug) in ALL modes, reproduced on untouched files, so it validates nothing. **Blocks H2** (snippet lint to 100%) and degrades the docs gate to link-check only. |
| X-semantic-pipelines-retire | T2 | — | pending | | | | retire/rewrite `docs/guides/semantic-pipelines.md` — it is wholesale deleted-Flow-pillar prose. A2 did the named ghost fixes + flipped `status: draft`, but the guide needs a real rewrite against the current pipeline API or archival. |

## Divergence log

When a pre-flight fails or repo reality contradicts a card, record it here:
date · prompt ID · what was found · what was done instead.

| Date | ID | Finding | Action |
|---|---|---|---|
| 2026-06-13 | B1 | The card named only 5 husks to exclude. In reality 38 test projects were missing from the sln, and **13 more break the build** beyond the card's list: (a) 3 net8.0 husks referencing nonexistent `Koan.TestPipeline`/`Koan.*` NuGet packages — `Koan.Storage.Core.Tests`, `Koan.Web.Admin.Tests`; (b) 2 net8.0 sample-test husks that fail restore (`Koan.Samples.DocMind.Tests` refs a *missing* `samples/S13.DocMind/API/...csproj`; `Koan.Samples.PantryPal.Tests` is net8.0 vs net10.0 `Koan.Testing`); (c) 3 MCP/PantryPal projects fail NU1903 (transitive MessagePack 2.5.192 CVE — the 7e8a44f0 pin only covers projects that ProjectReference `Koan.Orchestration.Aspire`, which these don't): `Koan.Mcp.TestHost`, `Koan.Samples.McpCodeMode.Tests`, `S16.PantryPal.Tests`; (d) 4 projects fail **compilation** against current APIs — `Koan.Web.Sort.Tests` (CS7022 dup entrypoint), `Koan.Storage.Connector.Local.Tests` (stale `StorageProfile`/`StorageOptions`/caps), `S7.Meridian.Tests` (stale `IEmbeddingCache`/`CachedEmbedding`/`IDocumentStorage.Delete`), and a **production** project `src/Services/code-intelligence/Koan.Service.KoanContext` (does not compile; was dragged in transitively by `Koan.Tests.Context.Unit`). | Added the 25 buildable live test projects (+2 transitive prod refs `Koan.Cache.Analyzers`, `Koan.Data.Connector.PGVector`). Per STOP condition, did NOT fix-forward any broken project; excluded all 13 breakers (and orphaned transitive pull-ins `samples/S7.Meridian`, `Koan.Service.KoanContext`). Build green; `--list-tests` exit 0. These 13 are husk/rot candidates for the C-series cut cards — they fell out of the sln precisely because they don't build. |
| 2026-06-13 | B1·CVE | (B1 follow-up, per architect decision) `StreamJsonRpc 2.22.23 → MessagePack 2.5.192` is a **second** CVE-2026-48109 root not covered by `7e8a44f0`'s Aspire-only pin: 12 NU1903 warnings fleet-wide + 3 unbuildable MCP test projects. | Pinned MessagePack 2.5.301 in `Koan.Mcp` (the sole direct StreamJsonRpc referencer) → NU1903 = 0; recovered `Koan.Mcp.TestHost` + `Koan.Samples.McpCodeMode.Tests` into the sln (195→197). Commits `b8530a30`, `28bb32ba`. Also fixed the malformed XML comment that broke `dev`'s build (`dcc1477c`). |
| 2026-06-13 | S16.PantryPal.Tests | The CVE pin recovered 2 of B1's 3 MCP breakers, but this one has rot **beyond** the CVE: stale FluentAssertions v6 API + a CS1997 async `DisposeAsync` bug. | Left excluded from the sln; logged as card **X-FluentAssertions**. Not fix-forwarded — separate concern from the CVE. |
| 2026-06-13 | A1 | Card prescribes the banner "Flow pillar removed from the codebase" and names "DATA-0019 (Cqrs)". Reality: Flow is **mid-migration to Canon** (`Koan.Canon.Domain`/`.Web` still in the sln; `Koan.Flow.Core`/`Koan.Canon.Core` dirs exist) — not removed; and `DATA-0019` is the **Outbox** ADR, there is **no CQRS ADR**, while `src/Koan.Data.Cqrs` still exists. | FLOW banners reworded to the truthful "superseded by the Koan.Canon rebuild"; DATA-0019 skipped (both act-conditions unmet). Upstream gap flagged: the CQRS decision has no ADR. |
| 2026-06-13 | A3 | Card estimated "~9" `*.csproj.lscache`; actual **103** (one per project). Deleting `ZERO-CONFIG-ANALYSIS.md` orphaned an inbound link at `docs/decisions/DEC-0053:339` — a **cross-card artifact** (A3 deletes; the link lives in A1's lane). | Deleted all 103 (gitignored). Orchestrator repaired the DEC-0053 link during integration (folded into A3 commit `ad13b65c`). Confirms: concurrent cards can create artifacts neither owns — the integrator resolves them. |
| 2026-06-14 | F1 | First code-adding card under the strengthened test-adequacy gate. The implementation was faithful and its 2 initial ARCH-0079 specs were mutation-proof — but the gate's **coverage critic** found the **manifest-invoker fail-loud path** (the one whose failure "can silently no-op the ENTIRE framework") and the **MODULES-FAILED render** were UNTESTED. A careful agent still missed the most dangerous path. | Remediated: +5 specs (manifest fail-fast/lenient, render +/-, KoanBootException field/message, clean-boot guard) + a minimal null-by-default internal test seam (the source-gen loader swallows everything, so the branch is otherwise unreachable). Orchestrator **independently mutation-checked** every new spec (revert→fail, restore→pass). F1 passes only AFTER remediation. Lesson: comprehensive integration tests are not automatic — the coverage-critic + mutation-check gate is what makes "wrote tests" mean "tested the dangerous paths". |
| 2026-06-14 | wave-2 docs (A2/H5/H7, parallel) | (a) `scripts/validate-code-examples.ps1` is **broken** (SwitchParameter crash, line 219) in all modes — snippet-lint validates nothing. (b) `set` drifted → folded into `partition` (DATA-0077); `BootReport` is no longer a type (rendered provenance). (c) A2's heading rename broke an inbound deep link from out-of-lane `docs/examples/entity-pattern-recipes.md:40` (A2 left a backward-compat anchor shim). (d) `semantic-pipelines.md` is wholesale deleted-Flow prose. | 3 cards ran in parallel cleanly (disjoint lanes); glossary anchors `set`→partition + BootReport→renderer; orchestrator fixed the pre-existing `index.md → reference/index.md` broken link at integration. New cards filed: X-snippet-lint-fix (blocks H2), X-semantic-pipelines-retire. `DataSetContext.With(set)` ghost in comparison.md deferred. |

## Operator gates

When a card surfaces a post-merge action that lives outside the repo (a prod migration, a CI
secret, a manual deploy step), append a `### <ID> operator gate` subsection here describing it,
so the operator has a single place to find pending out-of-band work. None yet.
