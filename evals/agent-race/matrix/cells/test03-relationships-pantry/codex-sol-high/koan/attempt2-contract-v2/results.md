# Results — test03 relationships-pantry · codex-sol-high · koan arm (contract v2, skill v6)

- Harness: codex-cli 0.150.0, `gpt-5.6-sol` @ high, unattended; treatment: skill v6 pointer;
  **contract v2** (pinned response shapes, auto-created ingredients, pantry envelope
  `{title, missingCount, missing[]}`)
- Outcome: **7/12** — pass: build, start, create-recipes (6/6 POSTs 2xx), pantry-missing-empty-list,
  restart-persistence, semantic-probe-1, semantic-probe-2.
  Fail: **usage-count-milk (got 0)**, **conversion-filter**, **stat-over10 (got 0)**,
  **pantry-full-match-first (first=Big Feast, missing=0)**, **pantry-rank-last (last=Veggie Mix,
  missing=0)**.

## Finding — the hollow relationship layer

Contract v2's looser create shape (lines embedded, ingredients auto-registered) produced an app
whose **CRUD and semantic faces work while the relational face is hollow**: usage-count returns 0,
the conversion filter returns the wrong set, and the pantry match reports every recipe as fully
covered — the matcher is not comparing coverage at all. Everything a casual smoke test would
catch is green; every check that requires the recipe→ingredient relationship to *mean something*
fails.

Under contract v1, the same model+arm built working relationship lines (usage-count=3, conversion
✓, stat ✓) but tripped on the unpinned match shape. The v1↔v2 contrast is the finding: **when the
contract does not force the relational reads, the frontier agent silently degrades them** — the
exact silent-degradation pattern the framework's corrective-failure culture exists to prevent.

Both attempts are retained: v1 at `attempt1-contract-v1/` per arm (koan v1 graded 9/12 with
working relationship queries; contract ambiguity), v2 here (working CRUD/semantic; hollow
relationships). The pair is not yet complete (plain v2 pending).

## Grader note

The v2 grader initially crashed on a dropped `ALLC` assignment (heredoc loss); fixed and fully
re-run against the preserved app — the 7/12 above is the complete fixed-grader result.
