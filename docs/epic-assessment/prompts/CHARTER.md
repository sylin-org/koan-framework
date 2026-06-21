# Epic Prompt Stack — Session Charter

Every prompt in this directory begins with: **"Read CHARTER.md in full before anything
else."** This file is the shared contract. It exists because the executing agent may have no
other context — no memory of prior sessions, no access to the conversation that produced
this analysis, possibly a different provider. Everything you need that is not in your prompt
file is here or in the documents this folder ships with (`../00`–`06`).

## [PATHS] — resolve before starting

Three sibling repositories. Confirm each exists before working; if a path differs on this
machine, ask the operator once, then proceed.

```
KOAN = f:/Replica/NAS/Files/repo/github/sylin-org/koan-framework   (.NET 10 framework)
ZEN  = F:/Files/repo/github/sylin-org/zen-garden                   (Rust fleet orchestrator)
KOI  = F:/Files/repo/github/sylin-org/koi                          (Rust LAN substrate)
EPIC = <the folder containing this file's parent>                  (this analysis, portable)
```

Per-repo knowledge bases (read the relevant one before touching a repo):
- `KOAN/docs/assessment/` (esp. 06-prompt-stash.md preamble) and `KOAN/CLAUDE.md`
- `ZEN/docs/notes/assessment-2026-06/` and `ZEN/.agentic/` (rules + prompts)
- `KOI/docs/assessment/` and `KOI/docs/prompts/CHARTER.md`

## The mission (governs every choice)

These are open-source projects with a social mission: **capacitation** — enabling
individuals and small teams to take on capabilities usually denied them (self-hosting,
compute sovereignty, production-grade infrastructure without an enterprise budget, an IT
department, or a cloud account). They are **enablers, not competitors**: export in the
incumbent's formats, never require import in ours; consume what users already wrote; be the
substrate, not the surface; every capability needs an exit; degrade gracefully when a layer
is owned. When a design choice trades our surface against feeding an existing tool the user
already runs, the feeder posture wins. Honesty is the product: the audience being
capacitated is the one most harmed by fictional docs and silent failures.

## Canon you must not violate (summary; full text in ../04 and STACK-0001 once E01 lands)

1. **Layering law**: Koi → Zen Garden → Koan, strictly acyclic. Knowledge flows up; **names
   never flow down** (Koi code/docs must not name its consumers; ZG never depends on Koan;
   Koan touches siblings only via network contracts and satellite packages — no mainline
   compile-time sibling references, ever).
2. **Coupling form**: "works alone, lights up together." Reference implementation:
   `KOAN/src/Connectors/Data/Mongo/MongoOptionsConfigurator.cs:74-93` (autonomous fallback).
   Never add a hard sibling dependency to anything mainline.
3. **Frozen constants**: the HKDF domain-separation byte strings are one immutable
   `b"koi-…-v1"` namespace — `b"koi-unlock-slot-totp-v1"` (`KOI/crates/koi-crypto/src/unlock_slots.rs`),
   `b"koi-promote-v1"`, `b"koi-seal-group-v1"` (`key_agreement.rs`). Each is a frozen v1
   value: a new algorithm gets a new versioned label, never a rename/reuse. They were renamed
   once from the original `pond-*` strings in the pre-1.0 greenfield window (no production vault
   existed); frozen at `koi-*` from here.
4. **The Koi TLS proxy is outside all contracts** until it has data-plane tests and a
   truthful `status()`. Do not build anything on it; do not delete it either.
5. **The garden mesh (UDP 7184, `stone_chirp`/`tools_beacon`) is Zen Garden-internal** —
   never a cross-project contract.
6. **No mono-repo, no shared cross-language libraries, no ports across the Rust/.NET split.**
   Cross-language sharing happens only as protocols, JSON schemas, and conformance fixtures.
7. **Two trust fabrics, one binding**: Koi certmesh = machine/channel identity; Koan
   Security.Trust/KSVID = workload/agent identity. Never merged, never independent.
8. **Private downstream solutions exist** outside these three repos and exercise their
   surfaces. Refer to them ONLY as "private downstream solution." Never name them, never
   guess at their names, never search for them. This is a hard rule.

## Session protocol

0. **Claim your work.** Open [PROGRESS.md](PROGRESS.md). Confirm your prompt's prerequisites
   are actually `done` (verify in the repos, not just the table — it can lag). Set your row
   to `in-progress` with today's date and your model id. If a prereq is not truly green,
   stop and pick a runnable prompt instead (the Readiness section lists them).
1. **Research first.** Read the prompt's Context block, then the cited files at the cited
   lines. Re-verify every load-bearing claim before acting on it — the repos move; line
   numbers drift; treat citations as starting points, not gospel.
2. **Plan of record.** Write a short plan (in your working notes or PR description) before
   editing. If the prompt has a DECIDED block, those choices are closed — do not relitigate.
   DEFAULT items may be changed with a one-paragraph justification recorded in the commit/PR.
3. **Greenfield posture.** These are pre-launch projects: replace, don't bridge. No
   `[Obsolete]`/deprecation shims, no dual paths; delete superseded code in the same session.
   The only backward contract is the test suite staying green and the frozen items above.
4. **Verify empirically.** Run the build, run the tests, run the probe against the real
   thing. Never conclude from reading alone when running is possible.
5. **Leave a guard at the door.** Any surface you touched gets a tripwire before you finish:
   a test that fails if it regresses, a status endpoint that tells the truth, a CI step. Then
   update the repo's `docs/SURFACES.md` row (created by E02): surface, exercising solution,
   last-exercised date (today), guard.
6a. **Close your row.** Update [PROGRESS.md](PROGRESS.md): set status (`done`/`blocked`/
    `postponed`), link commit SHAs (repo-prefixed if cross-repo), one-line note. If you hit
    a contradiction between the prompt and the repos, add a Divergence-log entry. If you
    produced operator follow-up (a baseline, a manual verification), append an operator-gate
    section with copy-paste commands.
6. **Document.** Update the docs your change makes stale, in the repo you changed. Every
   code claim you write into docs must be verified against the code in the same session
   ("if it's in the docs, it compiles/runs").
7. **Commit discipline.** Conventional commits (`feat:`/`fix:`/`docs:`/`test:`/`chore:`),
   logical groups, imperative subjects. Do not push unless the operator asked.

## Output contract

End every session with a summary containing: what changed (files + one line each), what was
verified and HOW (commands + results), guards left behind, SURFACES.md rows updated,
**your PROGRESS.md row closed (with commit SHAs)**, deviations from DEFAULT (with
justification), and anything discovered that contradicts this charter or the prompt's
Context block (report it — do not silently work around it).

## When blocked

If a prerequisite from the prompt's Prereqs line is not actually satisfied in the repos
(e.g., a crate version not published, an ADR missing), STOP that thread, state precisely
what is missing and which prompt provides it, and complete whatever independent parts of
your prompt remain. Do not improvise the missing prerequisite.
