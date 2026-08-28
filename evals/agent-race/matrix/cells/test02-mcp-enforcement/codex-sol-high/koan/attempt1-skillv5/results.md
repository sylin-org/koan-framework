# Results — test02 MCP-enforcement · codex-sol-high · koan arm

- Harness: codex-cli 0.150.0, `gpt-5.6-sol` @ high, unattended; treatment: skill v5 pointer
- Outcome: **13/13, LEAKS 0** — wall 653 s

The full adversarial battery held: anonymous MCP `tools/list` contained no mutation tool
(advertisement-is-enforcement); an anonymous write attempt was refused; `cost` appeared in member
surfaces only — HTTP and MCP both; member write through MCP landed. HTTP battery: anonymous read
with `cost` absent, anonymous write denied, member CRUD green.

Verdict for the pair: the koan arm's declarations (`[Access]` + `[McpEntity]`) produced a
zero-leak governed MCP surface on the first attempt, in 11 minutes, with no security code written
by the agent.

Transcripts: `transcripts/events-s1.jsonl`.
