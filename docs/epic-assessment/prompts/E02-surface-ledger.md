# E02 — Surface Ledger + Rotation Contract

**Repo(s)**: all three · **Phase**: A · **Prereqs**: none (E01 helpful) · **One session**
Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Create the mechanical memory that makes the maintainer's serial-lane model safe: a
**surface ledger** (`docs/SURFACES.md`) in each repo recording which surfaces are exercised
by what, when last, and what guard protects them — plus the **rotation contract** (the
departure checklist). Motivating incident: Koi's TLS proxy worked, regressed silently at the
axum 0.8 upgrade while no downstream solution was exercising it, and `status()` kept
reporting `running: true` for months. A ledger row reading "proxy → private downstream
solution → \<date\> → none" would have made that a known risk instead of a discovered
fiction.

## Context

The maintainer matures surfaces by exercising them inside downstream solutions — the two
sibling repos and **private downstream solutions** outside them (refer to those ONLY by that
phrase; CHARTER rule 8). When the focus lane rotates away, unguarded surfaces rot. Full
rationale: `../02` §3 and `../06` §5. Sources for seeding rows: each repo's own assessment
pillar tables (`KOI/docs/assessment/2026-06-maturity-assessment.md` §3/§6,
`ZEN/docs/notes/assessment-2026-06/architecture.md` tiering,
`KOAN/docs/assessment/01-cartography.md` pillar verdicts) and git history for last-touched
dates.

## DECIDED

1. File: `docs/SURFACES.md` at each repo root's docs dir. Format — one table:
   `| Surface | Exercised by | Last exercised | Guard | Notes |`
   where *Exercised by* ∈ {a named in-repo sample/test suite, "zen-garden", "koan",
   "private downstream solution", "none"}; *Guard* ∈ {test path / CI job name / "none"}.
2. Header of the file = the **rotation contract**, verbatim short form: *"Before the lane
   leaves this repo or surface: tag; CI green; a tripwire exists for every surface the
   departing work was exercising; status endpoints tell the truth; this ledger is updated.
   Leave a guard at the door when you leave the room."*
3. Granularity: pillar/plane level (e.g. Koi: mdns, dns, certmesh, proxy, udp, truststore,
   dashboard, CLI manifest; ZG: offerings lifecycle, mongodb orch, ollama orch, pond,
   discovery, self-update, storage(parked), ai(pending succession ADR); Koan: per pillar —
   data inner ring, web nucleus, cache, jobs, vector, AI, MCP, Trust, ZenGarden bridge…).
   10–20 rows per repo, not 100.
4. Honesty rule: when exercise status is unknown, write "unknown since \<last git touch\>" —
   never guess "works".

## DEFAULT

- A tiny CI lint (where CI exists) asserting `docs/SURFACES.md` exists and the table parses.
- Seeding *Last exercised* from `git log -1 --format=%as -- <paths>` per surface.

## Plan of record

1. Draft the template once. 2. Per repo: enumerate surfaces from the assessment docs; seed
rows with git dates and known guards (e.g. Koi certmesh's 264 tests = guard exists; Koi
proxy = guard **none**; ZG's 2,483 tests exist but CI never ran → guard "tests, unwired").
3. Add the CI lint where a workflow exists. 4. Cross-link the ledger from each repo's agent
context file (`KOAN/CLAUDE.md`, `ZEN/.agentic/`, `KOI/.agentic/` or docs index). 5. Commit
per repo.

## Verification

Tables render; every row's *Last exercised* is a real date or "unknown since \<date\>"; the
proxy row in Koi reads guard=none (truth, until E08+/Koi work changes it).

## Definition of done

- [ ] `docs/SURFACES.md` committed in all three repos with seeded, honest rows.
- [ ] Rotation contract text at the top of each.
- [ ] Agent-context cross-links added; CI lint where applicable.
