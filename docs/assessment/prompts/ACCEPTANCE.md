# Acceptance gate & orchestration protocol

How a card's result is judged before its row in [PROGRESS.md](PROGRESS.md) may be marked `done`.
The orchestrator (the driving session) owns this gate; **a card subagent's self-report is
evidence, never a verdict** — every claim is independently re-derived.

## Roles

- **Card subagent** — executes exactly one card in a bounded context. Makes file changes +
  runs the card's own VERIFY. Never commits, never switches branches, never expands scope.
  Returns a structured report.
- **Orchestrator** — re-runs verification from scratch, runs the integration boundary, spawns
  the adversarial review, decides PASS / BLOCK / STOP, and handles all version control.

## The gate (three layers — all must hold for PASS)

### 1. Mechanical (hard, fully automated — non-negotiable)
- [ ] `dotnet build Koan.sln` green (scoped suite build during work; **full-sln green at the gate**).
- [ ] Card's named tests green (`tests/Suites/<area>`); container-gated specs skip cleanly, never fail.
- [ ] No new compiler warnings vs. the pre-card baseline.
- [ ] Every grep/STOP assertion the card names is satisfied and re-run by the orchestrator
      (e.g. "grep `Koan.Scheduling` = 0 hits").
- [ ] `scripts/docs-lint.ps1` + `scripts/validate-code-examples.ps1` green if docs/snippets touched.
- [ ] The diff is confined to the files the card names (no drive-by edits). Verified via `git diff --stat`.
- [ ] Conventional-commit message proposed; one card == one commit.

### 2. Koan ergonomics (grep + a Koan-principles reviewer agent)
- [ ] No manual `IRepository<T>`, no repository injection into services, no manual framework
      service registration outside `KoanModule`/`KoanAutoRegistrar`.
- [ ] `Entity<T>` grammar preserved; canonical verbs (Save/Remove/Query); Newtonsoft canonical.
- [ ] Capability differences are declared tokens — negotiated, never faked to a floor (no-stopgaps rule).
- [ ] Reference = Intent intact; any new surface self-registers; boot-report line present if the card specifies one.
- [ ] ARCH-0079: any new adapter/connector/pillar core ships ≥1 real-`AddKoan()` integration spec.
- [ ] Concept budget respected — no new public types beyond what the card scopes.

### 3. Architecture / DDD (adversarial review panel + human sign-off)
- [ ] Aggregate boundaries and ubiquitous language respected; seams clean.
- [ ] "Fewer but more meaningful parts" — the change reduces concept count or sharpens a seam, not the reverse.
- [ ] Reviewed by ≥2 role-diverse agents (clean-arch/DDD · Koan-principles · security where relevant),
      prompted to **refute**, not bless.
- [ ] **Frontier (T3) cards: human architect signs off.** A model panel narrows risk; it does not replace the call.

## Per-card-type addenda

- **cut / park** — target still exists (else STOP-and-report, not no-op); inbound `ProjectReference`
  count matches the card's EXPECTED-REFS; key public types have no live consumers; package not in
  any `.nuspec`; docs ledgers (`modules-overview`, `module-ledger`, `capability-map`) swept; named
  ADR annotated. `park` ⇒ moved to `/attic` + README line, NOT deleted.
- **migration / fold** — exactly one wiring unit remains; call sites updated to the canonical path;
  the deleted path has zero remaining refs (grep). Hot-path cards (singleflight, dispatch): keying
  + semantics preserved, called out explicitly.
- **docs / litter** — link integrity (`docs-lint`); every snippet compiles; deletions cite
  pre-deletion evidence (`git log --follow` for tracked files).

## Integration boundary & pause

After each card PASSes layers 1–2, the orchestrator runs the **integration boundary** for that
card's blast radius and then **halts for human assessment** before integrating:
- registration/boot-affecting cards (B*, cuts, E-series, F1) → `tests/Suites/Integration` (ARCH-0079
  bootstrap specs through real `AddKoan()`).
- pillar cards → that pillar's suite (`Data/<Adapter>`, `Web`, `Jobs`, …).
- docs-only cards → lint only (no integration run).

The pause surfaces: the diff (`git diff --stat` + notable hunks), the verification output, the review
panel's verdict, metrics (below), and any STOP/deviation. **No merge to `dev` without explicit OK.**

## Outcomes

- **PASS** — all three layers hold; orchestrator commits (conventional message) and updates the ledger row.
- **BLOCK** — a layer fails on something fixable within scope; orchestrator notes it, the card is
  re-run or hand-patched, never "fixed forward" into new scope.
- **STOP** — the card hit a STOP condition (stale evidence, target moved, ambiguity). Recorded in the
  PROGRESS Divergence log; the card is re-planned, not forced.

## Metrics captured per card (feeds the scale/no-scale decision)

`build wall-clock · scoped-vs-sln test time · STOP-and-report? · review findings (count/severity) ·
merge-conflict on integrate? · tokens · human-touch required?`
