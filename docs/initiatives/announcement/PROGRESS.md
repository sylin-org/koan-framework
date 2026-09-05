---
type: ARCHITECTURE
domain: framework
title: "Announcement Initiative Progress"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.30
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: initiative ledger
---

# Announcement Initiative Progress

This is the initiative's only live status ledger. Update it in the same change that starts,
blocks, or completes a work item. The roadmap describes order; it does not report progress.

## Initiative state

- Overall: `active`
- Current tranche: `T1 — Artifacts` (Wave-0 preparation may start in parallel)
- Active work item: A03 remaining flagship scenes and recording; local baseline available
- Active child: none

## Ledger

### 2026-09-05 — A03 local baseline delivered; publication prerequisite found

- [ApprovalDesk](../../../samples/applications/SharedApprovals/ApprovalDesk/README.md) now runs a
  browser purchase workflow through submission, approval, and a recorded order. It is the canonical
  flagship source, alongside AE-01's foundation and ExpenseDesk consumer.
- [AE-01 evidence](../application-evolution/evidence/AE-01.md) records 163 package/HTTP/MCP checks
  and the browser walkthrough. Core repair `6e2aafc56` fixes startup rejection of ordinary private
  foundation identities; the proof used local Core 1.0.34 with published capabilities.
- A03's published-only gate therefore remains unmet until that fix is released. The recording,
  GIF, jobs, semantic-search scenes, and A11 comparison remain outstanding. Do not present this
  local fixture as a completed launch artifact or as independent adoption evidence.
- Canonical application baseline: `157f9053ec10cce86154c433be119f9a2d624e0e`.

### 2026-09-05 — A03 application baseline implementation started

- Canonical working path: `samples/applications/SharedApprovals/ApprovalDesk/`, within the
  shared-foundation experiment. The first baseline covers persisted purchase approvals and
  a usable interface; the recording and remaining flagship scenes keep A03's existing criteria.
- AE-01 owns `Foundation/` and `ExpenseDesk/` beside it. FirstUse supplies the closest pattern
  and retains its original first-use purpose. Baseline evidence will identify the actual revision.

### 2026-09-05 — Coordinate the flagship with application evolution

- Maintainer approved the [application evolution initiative](../application-evolution/README.md).
  A03's working domain is the approval desk, the equivalent small outcome allowed by its scope.
- A03 retains the flagship application and recording; AE-01 owns the shared foundation and
  second consumer. Establish and record A03's canonical source and baseline revision before
  reuse. A11's comparison pins its own application revision.
- This records planning and ownership only; no application, recording, or experiment is marked
  complete. Application evolution status lives in its own ledger. Existing launch gates and the
  benchmark campaign's publication boundary remain in force.

### 2026-08-28 — A11 first cut: LoC measured on the graded matrix pairs (4.0× and 5.2× application LoC)

- Measured both fully-graded staged-composite pairs preserved under `evals/agent-race/matrix/
  cells/`: **claude-default 152 vs 608** (4.0×) and **agy-gemini 74 vs 384** (5.2×) application
  LoC, same 22-check battery passed on both arms of each pair — behavioral equivalence is
  grader-attested. A third pair (codex-sol-high) is plain-side-countable only (318); its koan
  code was not preserved.
- Maintainer review corrected the receipt's framing (the first draft called the controller
  "the full governed REST surface" — wrong): the REST surface IS the grammar's one line
  (`EntityController<Recipe>` inheritance); the controller's remaining mass is the task
  contract's own custom surface — search endpoint, a collection-filter override composed
  through the base pipeline (~65 counted lines vs the plain arm's ~168 for the same job) —
  plus a 7-line PUT route-id workaround for the gap WEB-0073 has since closed, which is dead
  weight on 1.0.30 and should not appear in the curated A03 app. The per-hit `Recipe.Get` in
  search was checked and is required: `VectorMatch<TKey>` carries id + similarity only, no
  hydration (framework-backlog question, not agent error). Receipt decomposition added.
- The composition carries the claim better than the ratio: the koan app is five `.cs` files
  (Program.cs 6 lines; entity 46; controller ~65 of custom task surface over the one-line
  REST grammar; search wiring 35), while the plain arm spent an
  eight-file, ~204-line hand-rolled `Embeddings/` stack (client, options, vector math,
  documents) plus the conventional DTO/DbContext/validator/schema-bootstrapper scaffolding to
  reach parity. Zero migrations both sides; config keys in the same band (koan 13–15, plain
  10–12 — the Ollama endpoint appears on both).
- Method and the counter script are committed beside the draft
  ([LOC-receipt-draft.md](work-items/artifacts/LOC-receipt-draft.md),
  [loc-count.py](work-items/artifacts/loc-count.py)); numbers are provisional until A03's
  committed demo app and the stock-guidance plain twin supersede the untracked cell code.
- Claim discipline held: these numbers are receipt-backed and quotable only with the draft's
  method and provenance (agent-built arms; plain twin under stock guidance still to come).
- Trap recorded: `.gitignore:20` (`artifacts/`) silently ignores **any** directory named
  artifacts — including `work-items/artifacts/`. The A04 card's "draft texts committed under
  `work-items/artifacts/`" has never been true (the Substack essay is untracked), and the
  receipt files needed `git add -f`. Root fix — a scoped negation rule or relocating draft
  artifacts out of an ignored name — is an operator decision; `.gitignore` carries another
  session's uncommitted changes, so it was not touched here.

### 2026-08-28 — Re-scope: benchmark effort moved out of the initiative; launch stands on receipts in hand

- **Operator decision.** The agent-race benchmark effort left the public initiative. Cards A01
  and A02 were deleted from `work-items/`, and the 2026-08-27→28 campaign ledger entries moved
  verbatim to the maintainer-local notes (untracked):
  `local/initiatives/announcement-benchmark/`. Nothing was rewritten or lost. The eval campaign
  itself continues in-tree under `evals/agent-race/` on its own schedule — its pending action
  (fire `run-test03.sh koan` then `plain` unchanged when the GPU window opens, then grade) is
  recorded there and in the latest commit message, untouched by this re-scope.
- **Consequence for claims.** No performance or agent-productivity claim publishes. The
  charter's claim 3 is re-stated as "terse, legible applications" with the terseness receipt
  (A11 — application LoC, plain twin, one-command reproduction) as its receipt; "agent-amplified
  development" is a held hypothesis pending a publishable receipt. A09 was rewritten as a
  staged-wave runbook (rehearsal → r/dotnet + article → Show HN) whose comment policy fits a
  one-maintainer project: replies happen when the maintainer is available; no windows, no
  public SLA.
- **Baseline re-measured (2026-08-28).** NuGet family 205 packages / 197,749 downloads
  (+16,347 in one day); `Sylin.Koan` bundle 2,141 (617 on 1.0.x); Templates 1,412; GitHub 4
  stars / 3 forks / 0 watchers unchanged; referrers still github.com only; zero social
  footprint (HN Algolia 0 hits; no external csproj references repo-wide). A one-day delta of
  that size against a single-entry referrer table is self-traffic (release train, CI, eval
  restores) — recorded in the charter as the concrete reason NuGet velocity is not a launch
  metric. A09/A10 use contamination-resistant instruments instead; gross numbers remain
  subtractable from eval run records.

### 2026-08-27 — Initiative opened; baseline captured

- Initiative created: charter, roadmap, acceptance gate, ledger, handoff, work items A01–A10.
- Pre-announcement baseline recorded into [CHARTER.md](CHARTER.md) from live sources:
  4 stars / 3 forks / 0 watchers; 14-day traffic 18 views (7 unique) vs 887 clones (196 unique,
  ~90 CI runs in window); referrers github.com only; NuGet family 181,402 downloads across 201
  packages with the `Sylin.Koan` bundle at 2,042 and `Sylin.Koan.Templates` at 1,191; zero
  social/community footprint anywhere.
- Positioning research recorded in the charter's environment section: ASP.NET Boilerplate
  end-of-support (May 2026), MCP standardization with governance as the live concern, .NET 10 LTS,
  and the "Koan" search-name collision hazard.
- Superseded 2026-08-28: the benchmark campaign this entry pointed at (A01 onward) moved to
  `local/initiatives/announcement-benchmark/`; see the re-scope entry above.
