# MCP enforcement — the adversarial column

The task: a recipe API where **anonymous callers can read, members can write, and the `cost`
field is member-only** — exposed to MCP clients under exactly those rules. Both arms get the same
outcome contract; the test is whether the implementation *leaks*.

## Why this column exists

Speed tests on trained terrain favored the plain control. This test measures a different axis:
**correctness under adversarial probing**. The koan claim under test is structural — *advertisement
is enforcement*: a caller's tool list contains only what its identity may use, and a forbidden
field simply does not appear. The control must invent server, schemas, identity gating, and field
projection — and the grader measures whether that invention holds.

## The contract (identical text in `task-mcp.txt` for both arms)

- Entity: recipes with `id`, `title`, `ingredients[]`, `instructions`, `cost` (decimal).
- Auth: `X-Api-Key` header. `member-key` → member (full CRUD, sees `cost`). Any other/absent key →
  anonymous (read-only, never sees `cost`).
- MCP: a server reachable over **Streamable HTTP at `/mcp`**; MCP clients must be able to list and
  read recipes; members may also create/update/delete; anonymous MCP callers must not be able to
  write and must not see `cost`.
- HTTP surface mirrors the same rules. Persistence in SQLite, port **5097**, offline.

## The grader (HTTP + JSONRPC only; never reads the implementation)

Two MCP sessions are opened against the implementation: **anonymous** and **member**
(`initialize` → `notifications/initialized` → `tools/list` → `tools/call`). Then:

1. **HTTP battery** — anonymous read (no `cost` anywhere in the response), anonymous write →
   denied, member CRUD incl. `cost`.
2. **MCP advertisement** — member `tools/list` includes read + write tools; anonymous `tools/list`
   contains **no mutation tool** (lenient name match on create/update/delete/write markers).
3. **MCP enforcement-by-absence** — anonymous `tools/call` against any advertised write-shaped
   tool → refusal, never a silent write.
4. **Field projection** — anonymous MCP read output must not contain `cost`; member read output
   must.
5. **HTTP/`cost` leak hunt** — anonymous list + by-id responses must not contain the seeded cost
   value.

A leak on any of 2–5 is recorded as a **defect count**, not just a failed check — the matrix
graphs leaked surfaces per implementation, which is the quantity no speed test measures.

## Fairness

- Task body byte-identical across arms; only the arm line differs (koan skill pointer vs plain,
  no `Sylin.*`).
- Tool *naming* is the implementer's choice; the grader matches names leniently (marker words) and
  documents every match it relied on in the run record.
- The MCP client speaks Streamable HTTP (initialize → initialized notification → tools/list →
  tools/call), one session per role, `mcp-session-id` honored.

## Status

- Task + grader: written (`task-mcp.txt`, `grade-mcp.sh`).
- Runners: mirror `tasks/staged-composite/run*.sh` with this task's files, port 5097, and the arm
  lines below — materialize when the column executes.
- Sequence: after the qwen38 v5 A/B completes (GPU + port 5099 are that run's resources).
