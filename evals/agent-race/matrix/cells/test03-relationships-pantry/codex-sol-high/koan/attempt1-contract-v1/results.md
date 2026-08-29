# Results — test03 relationships-pantry · codex-sol-high · koan arm (attempt 1)

- Harness: codex-cli 0.150.0, `gpt-5.6-sol` @ high, unattended; treatment: skill v6 pointer
- Outcome: **9/12** — failed: create-join, pantry-full-match-first, pantry-rank-last;
  passed: build, start, seed, usage-count-milk (=3, correct), conversion-filter (300 ml threshold
  across units, correct), stat-over10 (=1, correct), restart-persistence, both semantic probes

## Reading

The relational **queries the task was designed to probe all passed**: usage-count by name, the
cross-unit conversion filter (480/300 ml in, 15 tbsp out), the >10-ingredients stat, and both
keyword-disjoint semantic probes. The failures cluster on (a) the create-with-embedded-lines
shape and (b) the pantry-match response shape — and **the plain arm failed the same two pantry
checks with the same signature**, which points first at contract/grader shape ambiguity rather
than per-model failure. Investigation queued: boot both preserved apps, diff their create and
match response shapes against the grader's assumptions, then either loosen the grader's shape
handling or sharpen the task contract — whichever the evidence supports. Re-grade follows the fix;
this attempt is not counted as a model verdict.

Transcripts: `transcripts/events-s1.jsonl`.
