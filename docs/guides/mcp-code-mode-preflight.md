---
type: ENGINEERING
domain: engineering
title: "MCP Code Mode Preflight"
audience: [developers]
status: draft
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  status: not-yet-tested
  scope: docs/guides/mcp-code-mode-preflight.md
---
---
title: "MCP Code Mode Preflight Patterns"
---

# MCP Code Mode: Preflight Patterns

Short, instruction-first patterns for reducing failed executions and wasted quota when using Koan MCP Code Mode.

## Contract Block
- Inputs: JavaScript source (string)
- Outputs: Validation result `{ valid: boolean, error?: string }`
- Error Modes: None (validation tool always returns `Success=true` envelope)
- Side Effects: No entity calls, no mutations, no quota counters incremented
- Success Criteria: Agents only submit `koan.code.execute` when `valid=true`

## Tools
| Tool | Purpose | When to Use |
|------|---------|-------------|
| `koan.code.validate` | Parse-only syntax check | Before first execution or after edits |
| `koan.code.execute` | Execute script with SDK bindings | After a successful (or skipped) preflight |

## Basic Flow
1. Draft script (LLM or human).
2. Call `koan.code.validate` with `{ code }`.
3. If `valid=false`, refine using `error` message.
4. On `valid=true`, call `koan.code.execute`.
5. Inspect diagnostics (`sdkCalls`, `cpuMs`) to tune subsequent scripts.

## Example (Client-Side JSON-RPC)
```jsonc
// tools/call → koan.code.validate
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "tools/call",
  "params": {
    "name": "koan.code.validate",
    "arguments": { "code": "function run() { SDK.Out.answer('ok'); }" }
  }
}
```
Response:
```json
{
  "result": {
    "valid": true
  }
}
```
Then execute:
```jsonc
// tools/call → koan.code.execute
{
  "jsonrpc": "2.0",
  "id": "2",
  "method": "tools/call",
  "params": {
    "name": "koan.code.execute",
    "arguments": { "code": "function run() { SDK.Out.answer('ok'); }" }
  }
}
```

## Failure Example
```jsonc
// Missing brace
{
  "jsonrpc": "2.0",
  "id": "v1",
  "method": "tools/call",
  "params": {
    "name": "koan.code.validate",
    "arguments": { "code": "function run( {" }
  }
}
```
Response:
```json
{
  "result": {
    "valid": false,
    "error": "Syntax error at line 1, column 16: Unexpected token '}'"
  }
}
```

## Edge Cases
| Case | Validation Result | Notes |
|------|-------------------|-------|
| Empty string | `valid=false` + `Code cannot be empty` | Prevents empty execute calls |
| Oversized script | `valid=false` + length message | Mirrors `code_too_long` rule without consuming execution quota |
| Unterminated comment | `valid=false` + syntax diagnostic | Message originates from Jint parser |
| Valid but no `run()` | `valid=true` | Execution will still auto-scan for `run()`; absence is allowed |

## Why Preflight?
- Cuts retry loops (syntax errors surface earlier, outside quota enforcement)
- Keeps `CodeModeErrorCodes` space clean for semantic / runtime failures only
- Enables agent self-repair before committing to an execution cycle

## Recommended Agent Prompt Snippet
```
Before executing JavaScript against Koan:
1. Call koan.code.validate with the draft code.
2. If valid=false, incorporate the error message exactly once and regenerate.
3. Only call koan.code.execute when valid=true.
4. Always call SDK.Out.answer(text) exactly once before the script ends.
```

## Non-Goals
- Static analysis (no semantic checking of SDK usage yet)
- Performance estimation (CPU/memory costs only surfaced post execution)

## Related
- ADR: AI-0014 (Code Mode) – Error codes and validation rationale
- Tool Surface Exposure: `McpExposureMode` (Code / Full)

---
Instruction-first: avoid tutorial drift. Keep scripts minimal; rely on runtime diagnostics for deeper issues.
