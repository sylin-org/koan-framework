# Session Launcher — how to run one Epic card

The orchestrator hands each fresh session a kickoff built from the template below: replace
`{{CARD_ID}}` and `{{CARD_FILE}}`, paste as the session's first message. **One card per
session.** The orchestrator validates the handoff before the next card is launched — so the
session's job is to do *this* card well and report honestly, not to run ahead.

This launcher is a forcing function around [CHARTER.md](CHARTER.md), not a replacement for
it. CHARTER holds the full contract (paths, mission, canon, protocol); the launcher
guarantees the session opens it, stays in scope, and returns a validatable handoff.

---

## Launcher template

> Copy from here to the end marker; fill the two placeholders.

```
You are executing ONE card from the Sylin Epic prompt stack — a sequenced program that
matures three sibling repos (Koi, Zen Garden, Koan) into an integrated, sovereign,
agent-ready stack. You have no prior context and that is expected: everything you need is in
the files below. An orchestrator will review your handoff before any further work proceeds,
so be precise and honest about what you did and did not achieve.

CARD: {{CARD_ID}}  ({{CARD_FILE}})

Do exactly this, in order. Do not skip steps; do not start editing before step 6.

1. Read  docs/epic-assessment/prompts/CHARTER.md  in full. It is BINDING. Resolve the three
   repo paths from its [PATHS] block and confirm each exists.
2. Read your card, {{CARD_FILE}}, in full.
3. Read  docs/epic-assessment/prompts/PROGRESS.md . Find your card's row and its Prereqs.
   VERIFY each prerequisite is genuinely satisfied in the repos (build/grep/inspect — the
   table can lag). If any prereq is not truly green, STOP: report exactly which one and
   which card delivers it. Do not improvise a missing prerequisite.
4. Skim  docs/epic-assessment/prompts/README.md  (dependency graph) and open the card's
   cited evidence files AT the cited lines. Re-verify the load-bearing claims — line numbers
   drift; treat citations as starting points.
5. POST YOUR PRE-FLIGHT (in your first reply, before editing): (a) prereqs checked + how,
   (b) any citation that did not match reality, (c) your plan of record as a short numbered
   list. Then set your PROGRESS.md row to `in-progress` with today's date and your model id.
6. Do the work per the card. Honor every DECIDED item exactly — they are closed architect
   decisions; do not redesign them. A DEFAULT item may be changed ONLY with a one-paragraph
   written justification recorded in your commit/PR and handoff.
7. Verify EMPIRICALLY: run the builds, tests, and probes named in the card's Verification
   section, plus any mutation check it specifies (break it → red → restore → green). Reading
   is not verifying. Capture the commands and their result lines.
8. Leave a guard for every surface you touched (a test that fails on regression, a truthful
   status, a CI step) and update  docs/SURFACES.md  in the repo(s) you changed.
9. Close your PROGRESS.md row: status + commit SHAs (repo-prefixed if cross-repo) + one-line
   note. Add a Divergence-log entry if repo reality contradicted the card. Append an
   operator-gate section (copy-paste commands) if you produced follow-up the operator must
   run.
10. Commit in logical groups using conventional commits (feat/fix/docs/test/chore). Do NOT
    push. Do NOT proceed to any other card.

HARD GUARDRAILS (violating any of these fails the card):
- SCOPE: do only this card. If you find other defects, LIST them in your handoff — do not
  fix them. Do not reformat or touch unrelated files.
- NO STOPGAPS: root fix only. If the correct fix is out of scope or blocked, STOP and report
  — never band-aid (e.g. never drop a capability to a floor to make a test pass).
- GREENFIELD: replace, don't bridge. No [Obsolete]/deprecation shims, no dual code paths;
  delete superseded code in the same session. The only backward contract is the test suite
  staying green plus CHARTER's frozen items.
- FROZEN: never alter the HKDF domain-separation byte strings in koi-crypto
  (the b"koi-…-v1" namespace: b"koi-unlock-slot-totp-v1", b"koi-promote-v1", b"koi-seal-group-v1")
  or anything CHARTER marks frozen. A new algorithm gets a new versioned label, never a
  rename. (They were renamed once from the original b"pond-*" strings in the pre-1.0 greenfield
  window; frozen at koi-* from here.)
- PRIVACY (ABSOLUTE): downstream solutions outside these three repos exist and exercise
  their surfaces. Refer to any such thing ONLY as "private downstream solution." Never write
  its name into any file, never guess it, never search for it.
- IRREVERSIBLE: stop and request operator confirmation before any irreversible act —
  `cargo publish`, deleting data/files you did not create, force operations, anything the
  card marks an operator gate. Local commits are fine; pushing is not (unless told).
- LAYERING: knowledge flows up, names never flow down (Koi must not name its consumers; Zen
  never depends on Koan; Koan touches siblings only via network contracts + satellite
  packages — never mainline compile-time references).
- HONESTY: if you cannot complete the card, mark it `blocked`/`partial`, finish whatever
  independent parts you can, and report precisely what remains and why. A truthful partial
  beats a false `done`.

EFFICIENCY (do not waste the budget):
- The cards carry the ground truth — do not re-run the whole assessment or re-derive the
  strategy. Read the cited lines, not whole large files; batch independent reads.
- Use each repo's own build/test tooling (named in CHARTER's per-repo knowledge bases).
- Keep commits tight and scoped; one card's work should read as one coherent change set.
- If the card offers a split (e.g. 6a/6b) and context is tight, do one split unit cleanly
  and mark the rest `pending` with a precise note rather than half-doing both.

END YOUR SESSION with this handoff block, filled in — the orchestrator validates against it:

## HANDOFF — {{CARD_ID}}
- Result: done | partial | blocked
- Commits: <repo>:<sha> (one line each, what it did)
- Definition of done: copy the card's DoD checklist; tick each [x] with one-line evidence,
  or mark [ ] and say why.
- Verified how: the commands you ran + the result lines that prove the card works (incl. the
  mutation check if the card specified one).
- Guards left: surface -> guard (test path / CI job / truthful status).
- SURFACES.md rows updated: which, in which repo(s).
- DEFAULT deviations: each change + its one-paragraph justification (or "none").
- Divergence: anything where the repo contradicted the card (or "none") — also logged in
  PROGRESS.md.
- Out-of-scope issues found (filed, NOT fixed): list (or "none").
- Next card may now assume: the concrete new facts (versions published, endpoints stable,
  types available) the downstream card depends on.
- Operator actions required: e.g. confirm a publish, provide two nodes, run a baseline (or
  "none").
```

> End of template.

---

## What the orchestrator checks (the validation gate)

A card passes when: every Definition-of-done item is ticked with real evidence; the
Verification commands were actually run and shown (not described); a guard exists for each
touched surface and SURFACES.md reflects it; PROGRESS.md is closed correctly; no HARD
guardrail was crossed; and the "Next card may now assume" facts are true (spot-checked).
Partial/blocked is a valid outcome — it routes to a fix or a divergence decision, not a
failure. Only after a pass does the next card launch.

## Notes for the orchestrator (not pasted into the session)

- Lead with **E01** for any new model tier — it is pure transcription, touches no hot path,
  and its DECIDED/DEFAULT handling calibrates trust for everything after.
- For cross-repo cards (E01, E02, E07, E11, E12, E15, E16) confirm the session truly has all
  three repo paths before launching; a session boxed into one repo will silently under-deliver.
- The two operator gates that will interrupt a session: **E03** (`cargo publish`) and **E11**
  (two nodes). Have the decision/resources ready when those cards come up.
