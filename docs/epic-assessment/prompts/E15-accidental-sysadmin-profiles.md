# E15 — The Accidental-Sysadmin Profiles

**Repo(s)**: all three (docs-led) · **Phase**: E · **Prereqs**: Phase B done (front-door
truth per-repo well underway — these guides must not inherit fictional steps) · **One
session** · Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Give the mission's center-of-gravity persona a front door: the **accidental sysadmin** —
the person at a small org with a duty of care and no IT department (community center,
clinic, school, nonprofit, co-op) who got handed the infrastructure job. Koi's own spec
names this audience; nobody's docs address them. Enterprise tooling assumes staff; homelab
tooling assumes hobby time. This persona has neither — they need conservative defaults,
checklists, and exits.

## Context

- Voice and shape: curriculum, not marketing. The reader is intelligent, busy,
  non-specialist, and accountable to other people. Every step says what it does, why, how
  to verify it worked, and how to undo it (the enabler doctrine's "every capability needs
  an exit" applied to documentation).
- Source truth: each repo's existing guides + the per-repo assessments' newcomer-walk
  findings (what confused/broke). **Every command in these guides is executed during this
  session before being written down** — the executable-front-door rule is non-negotiable
  for this audience.

## DECIDED

1. **One profile guide per repo + one stack umbrella here**:
   - `KOI/docs/guides/community-setup.md` — "names, certificates, and discovery for a small
     organization": one binary, the token model in plain language, `.internal` naming, the
     truststore step for managed machines, what to do when something breaks (status, logs),
     conservative bind/auth defaults (loopback + token; widen only deliberately per E05).
   - `ZEN/docs/guides/community-fleet.md` — "donated hardware becomes your org's compute":
     the laptop-to-stone path, the duty-of-care security baseline (pond on, E09 posture, no
     default passphrases), backups location, the pull-the-plug reassurance, when NOT to use
     this (the honesty section).
   - `KOAN/docs/guides/community-apps.md` — "one small app for your org, on your own box":
     the sovereign profile (E14) consumed as a recipe; what data lives where; the exit
     (your data is standard MongoDB; your API is standard HTTP).
   - `EPIC/profiles/accidental-sysadmin.md` — the umbrella: who this is for, the three
     layers in one page, an honest "what you're taking on" section (maintenance
     expectations, update cadence, the support reality of pre-1.0 software).
2. **Conservative defaults are stated as defaults, not options**: token auth on, loopback
   unless deliberately widened, mutations gated, backups configured in-guide (where the
   capability exists — never script around a gap; absent capability = honest "not yet"
   note).
3. **Each guide ends with a verification checklist** ("you are done when…") and an exit
   section ("to stop using this…").
4. **Pre-1.0 honesty banner** on all four documents — this audience must not be oversold;
   per-repo maturity statements link the repo's own assessment-derived status.

## DEFAULT

- Length: each guide ≤ ~200 lines; the umbrella ≤ 120. Cut features before cutting honesty.
- Where a repo's quickstart is still broken, scope the guide to the *working* path and say
  so, rather than waiting (note the dependency in SURFACES/issues).

## Plan of record

1. Read each repo's current entry guides + the newcomer-walk findings. 2. Walk each
target path live, capturing real commands/outputs. 3. Write the four documents. 4. Where CI
executable-docs gates exist, register the new guides' command blocks. 5. Cross-link: each
repo guide ↔ the umbrella. 6. SURFACES.md note per repo (guide ↔ exercised path ↔ guard).

## Verification

A clean re-walk of each guide verbatim (fresh shell/state) completes; checklists are
satisfiable; every "verify" step shows the documented output.

## Definition of done

- [ ] Four documents committed; every command executed-before-written.
- [ ] Conservative defaults, exits, and pre-1.0 honesty present in all four.
- [ ] Executable-docs registration where gates exist; SURFACES.md updated.
