# Results — test02 MCP-enforcement · codex-sol-high · koan arm · skill-version history

Current treatment: **skill v6** (v5 + verify-in-one-pass probe-script pattern).

| Skill | Wall | Battery | Leaks | Notes |
|---|---|---|---|---|
| v5 | 653 s | **13/13** | 0 | best measured |
| v6 | 838 s | 11/13 | 0 | member-MCP checks failed this run |

## v5 → v6 A/B verdict

**The verify-once sentence did not reduce wall time or cycles at n=1**: build/run cycles stayed
at 12/12, wall rose 653→838 s, and the two member-MCP checks (`cost` visible to members, member
write through MCP) failed this run after passing in v5. Zero leaks were maintained in both.

Honest reading: a single sequencing sentence does not move a frontier model's loop behavior —
variance (n=1 both sides) plus the possibility that the member-session failures reflect an
implementation choice the v6 agent made differently (role propagation into its MCP session)
means the A/B is inconclusive-to-negative, and is recorded as such. If the verify-once lever is
pursued further, it needs to be **mechanical** — a canonical probe script shipped beside the
skill that the agent runs, rather than a sentence asking for discipline.

Archived: v5 attempt at `attempt1-skillv5/` (13/13); v6 transcripts in `transcripts/`.
