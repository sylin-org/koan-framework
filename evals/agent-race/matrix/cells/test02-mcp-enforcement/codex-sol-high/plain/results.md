# Results — test02 MCP-enforcement · codex-sol-high · plain arm

- Harness: codex-cli 0.150.0, `gpt-5.6-sol` @ high, unattended; treatment: plain arm line
- Outcome: **11/13, LEAKS 0** — wall 568 s

## What worked

The agent built a real MCP server via the official C# SDK (`AddMcpServer()` + `MapMcp("/mcp")`)
in **stateless Streamable HTTP mode**, with member-gated tool descriptions ("Private cost data is
included only for members"). With the grader's session-header bug fixed (it sent a literal
`mcp-session-id: none` that stateless servers correctly reject), initialize, `tools/list`, and
reads all work. The HTTP battery passed fully: anonymous reads hide `cost`, anonymous writes are
denied, member CRUD works. **No leaks: zero mutation tools advertised anonymously, zero cost
values in anonymous surfaces, zero forged rows.**

## The two failures — recorded as grader artifacts, with the correction path

1. `CHECK fail build` — the preserved app was snapshotted *inside the repository tree* and now
   inherits the repo's Central Package Management; its csproj declares Versions directly
   (NU1008). The original build outside the repo was clean, and the preserved binary served
   requests throughout the re-grade. Fix: exclude cell snapshots from repo build config, or ship
   a neutral `Directory.Build.props` in snapshots.
2. `CHECK fail mcp-member-write-works` — the grader's lenient name-matcher picked the first
   write-shaped tool (`update_recipe`) and called it without the required `id` argument. The
   member *create* tool exists and the member HTTP write works; the heuristic mismatched the
   call shape. Fix: prefer create-shaped tools for the write probe, or pass the id the read
   returned.

## Pair verdict (single runs)

Koan 13/13 (653 s, zero security code written) vs plain 11/13 with zero leaks in 568 s. The
control's invention **held on this run** — trained on the official MCP SDK, it gated correctly.
What the pair still shows: the koan arm needed no security decisions at all, and its surface is
identical across every future run, while the control's correctness depends on what the model
assembles per run. n=1; the leak-rate question needs A02's repeats before any claim publishes.

Transcripts: `transcripts/events-s1.jsonl`; fixed-grader re-grade in
`transcripts/grade-stage1-fixed.txt`.
