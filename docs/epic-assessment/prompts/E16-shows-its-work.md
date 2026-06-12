# E16 — "Software That Shows Its Work"

**Repo(s)**: all three (docs + packaging of existing machinery) · **Phase**: E · **Prereqs**:
E02 (ledger), E12 (envelope); strengthens as truth gates land · **One session** ·
Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Package the stack's verifiability machinery into a named, public-facing trust posture. The
portfolio's biggest social liability — solo, AI-amplified development volume, now a
community trust tax (maintainers screen for it; one disclosed-AI project collapse and a
major project's burnout statement made it explicit in 2026) — is answerable by artifacts
these projects already build for other reasons. Assembled and *named*, "software that shows
its work" converts the liability into the portfolio's most distinctive trust feature — and
it is mission-aligned: the capacitated audience deserves to verify, not trust.

## Context (verify what exists before claiming it)

The candidate artifacts, per repo (link ONLY what is real at execution time):
- **Provenance & method**: Zen Garden's disclosed AI co-authorship analysis
  (`ZEN/docs/zen-garden-archaeology.md` — verify path) and honest-decision culture
  (dissolution ADRs, post-mortems); Koi's `.agentic/` tool-agnostic context + ADR corpus
  with honest reversals; Koan's ADR discipline + assessment corpus being public in-repo.
- **Self-description**: the E12 envelope endpoints; Koan boot reports; SURFACES.md ledgers
  (E02) — the candid "what's guarded, what's not" record.
- **Truth gates**: executable front doors / snippet-truth CI where landed; the green
  ratchet; contract corpora (E07).

## DECIDED

1. **One page per repo** (e.g. `docs/shows-its-work.md`, linked from each README): what
   this project lets you verify and how — *run the self-description endpoint, read the
   surface ledger, read the ADRs including the reversals, run the executable docs, here is
   the AI-assistance policy.* Concrete commands, not values statements.
2. **A written AI-assisted-development policy per repo** (short, factual): how AI is used,
   how output is reviewed and verified (the gates), how co-authorship is disclosed in
   commits, and what a contributor should expect. Zen Garden's existing disclosure is the
   template; normalize across the three. Refer to private downstream solutions only as
   that, if at all (CHARTER rule 8).
3. **No aspirational claims**: every linked artifact must exist and every command must run
   (execute them in-session). Where a gate is planned but not landed, it is listed under
   "coming, tracked at <issue/prompt>" — explicitly not claimed.
4. The umbrella statement lands in `EPIC/` (one page) describing the posture stack-wide,
   reusing the per-repo pages.

## DEFAULT

- Tone: engineer-to-engineer, zero marketing adjectives. The page's persuasive power is
  that it reads like evidence.
- Naming: "shows its work" as the working title; the operator may rename.

## Plan of record

1. Inventory which candidate artifacts actually exist per repo (run/read each). 2. Write
the three repo pages + policy sections; 3. the umbrella page here. 4. Link from READMEs
(one line each — do not restructure entry docs; that's per-repo stash work). 5. Execute
every command included. 6. SURFACES.md note (the pages themselves are docs-surfaces with
the executable-docs guard where it exists).

## Verification

Every command on every page executed with output captured; every link resolves; zero
claims about artifacts that don't exist.

## Definition of done

- [ ] Three repo pages + umbrella committed; READMEs link them.
- [ ] AI-assistance policy normalized across the three repos, factual and current.
- [ ] All commands executed; "coming" items explicitly marked; SURFACES.md noted.
