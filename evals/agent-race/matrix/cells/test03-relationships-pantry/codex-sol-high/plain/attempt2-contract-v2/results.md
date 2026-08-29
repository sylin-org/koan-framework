# Results — test03 relationships-pantry · codex-sol-high · plain arm (contract v2, skill v6 era)

- Harness: codex-cli 0.150.0, `gpt-5.6-sol` @ high, unattended; treatment: plain arm line
- Outcome: **12/12** — wall 597 s; tokens 1.68 M in / 24.7 K out

Everything passed: recipe CRUD with embedded ingredient lines, usage-count for milk (= 3), the
cross-unit conversion filter (480/300 ml in, 15 ml tbsp out), the >10-ingredients stat (= 1), the
pantry match fully-ranked (Salted Pasta first with missing 0; Big Feast last with missing 8,
garlic named), restart persistence, and both keyword-disjoint semantic probes.

## Pair verdict — contract v2 (single runs)

| Arm | Battery | Wall | Relational layer |
|---|---|---|---|
| koan (skill v6) | 7/12 | 780 s | **hollow** — usage-count 0, pantry trivially satisfied |
| plain | **12/12** | 597 s | **complete** — join modeling, counts, conversion filter, ranked match |

A complete reversal of test01's pattern: on the relationship-heavy task, the **plain arm beat the
koan arm on correctness, not just speed**. The mechanism is visible in the contract change:
v2's "ingredients do not need their own management endpoints; referencing by name auto-creates
them" is textually arm-neutral but **effectively steered the koan arm away from modeling
Ingredient as a first-class entity** — and without an Ingredient entity there is no `[Parent]`
edge to declare, no relationship query to make, and the governed surface offers nothing to
express usage counts with, so the agent shipped unindexed embedded lines behind a green CRUD
facade. The plain arm had no relationship surface to under-use, so it simply built the join.

## What this finding is

- A **real negative result for the koan arm** on relationship-heavy tasks under contract v2 +
  skill v6, at frontier tier, n=1 per arm.
- A **product signal for the framework**: the agent never encountered the relationship grammar —
  the skill's one-block (v6) does not mention `[Parent]`, and the promoted leaf was only written
  during this campaign. WEB-0073's lesson repeats one level up: capabilities an agent cannot
  find do not exist. Skill v7 (relationship compound in the one-block, per the test03 README's
  docs-gap note) is the direct counter, to be measured as its own treatment version.
- A **contract-design lesson**: arm-neutral text is not effect-neutral. Contract v2's
  ingredient-simplification line changed which structures each arm was likely to build.

Transcripts: `transcripts/events-s1.jsonl`.
