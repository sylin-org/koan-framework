# Designing a Code‑Mode Surface for Agentic AI (TS‑First, MCP‑Compatible)

**Primary reference**: https://www.youtube.com/watch?v=bAYZjVAodoo  
**Also see**: Cloudflare “Code Mode” proposal; MCP docs; OpenAI/Anthropic tool‑calling guidance (links at end).

---

## Executive Summary
Traditional “tool calling” (including MCP-first designs) degrades as the tool list grows: the model must select among many options, every tool call inserts more text into context, and multi-step flows require multiple LLM roundtrips. A better pattern is **code‑mode**: expose a **small, typed TypeScript SDK** plus **one tool** (`executeCode(ts|js)`), let the model write short programs that orchestrate calls deterministically in a **sandbox**, and return only the final results. This yields higher reliability, fewer tokens, and simpler governance.

**Thesis**
- Prefer **TypeScript‑typed SDK** as the agent’s “work surface”.  
- Provide **one execution tool** bound to a secure sandbox with only those capabilities.  
- Optionally **interop with MCP** by transforming MCP tool schemas into a TS façade for the same code‑mode runtime.

---

## Goals & Non‑Goals
**Goals**
- Improve success rate on multi‑step tasks; reduce latency and token costs.  
- Reduce cognitive load: *one* execution tool, *one* SDK to learn.  
- Enforce capability security via hard sandbox boundaries and typed contracts.  
- Offer a clean migration path from MCP or direct tool‑calling stacks.

**Non‑Goals**
- Replacing all streaming/interactive patterns; direct tools can still be useful for very small, single‑call actions.  
- Exposing the full underlying platform; the SDK should be **minimal and orthogonal**.

---

## Architecture Overview
```
[User]
  └─▶ [Planner/System Prompt]
       ├─ Provides: TS SDK types + JSDoc
       └─ Exposes: executeCode(code: string)
            └─▶ [Sandbox / Isolate Runtime]
                 ├─ Binds SDK capabilities (Files, HTTP, KV, Memory, Out, etc.)
                 ├─ Enforces limits (CPU, memory, I/O, net egress)
                 └─ Emits structured logs & result
```
**Key characteristics**
- **Single tool** (`executeCode`) → fewer decisions, fewer roundtrips.  
- **Typed surface** (TS) → clearer affordances; models follow enums/unions/literals well.  
- **Deterministic composition** → multi‑call logic in code, not in the LLM’s hidden state.  
- **Hard security boundary** → capability‑based bindings inside the isolate.

---

## SDK Design Principles
1) **Small surface area** (3–7 domains): e.g., `Memory`, `Files`, `HTTP`, `KV`, `Time`, `Out`.  
2) **Orthogonality**: each domain does one thing well; compose in code.  
3) **Strong types**: branded IDs, literal unions, discriminated unions, narrow return types.  
4) **Idempotency & timeouts**: clearly defined for each operation.  
5) **Pure vs. impure** separation: reads vs. writes are obvious.

### Example `.d.ts` the model can see
```ts
// app-sdk.d.ts
declare namespace App {
  type Zip = `${number}${number}${number}${number}${number}`;
  interface User { id: string; zip: Zip }

  interface Weather { getToday(zip: Zip): Promise<{ hi: number; lo: number; rain: boolean }>; }
  interface Memory { getUser(): Promise<User | null>; getPref(key: string): Promise<string | null>; }
  interface Files { read(path: string): Promise<string>; write(path: string, contents: string): Promise<void>; }
  interface Out { answer(text: string): void; info(text: string): void; }
}
export const App: { Weather: App.Weather; Memory: App.Memory; Files: App.Files; Out: App.Out };
```

### Model‑authored code (runs as JS or TS)
```js
/** @typedef {import('./app-sdk').App} App */
export default async function run({ App }) {
  const u = await App.Memory.getUser();
  if (!u) return App.Out.answer("I don’t have a user on file.");
  const wx = await App.Weather.getToday(u.zip);
  const coat = (wx.rain || wx.lo < 50) ? "light jacket" : "t‑shirt";
  App.Out.answer(`Hi ${u.id}, wear a ${coat}. High ${wx.hi}°F, low ${wx.lo}°F.`);
}
```

---

## Execution & Result Protocol
- **Input**: code string (TS or JS). If TS, transpile (esbuild/swc) before execution.  
- **Sandbox**: V8 isolate/Worker/Deno/Node VM with **no ambient FS/NET/PROCESS**; all egress via bound SDK.  
- **Outputs**: structured envelope `{ logs, metrics, result }` and human‑readable message via `Out.answer`.  
- **Error surfacing**: include pretty stack traces with mapped TS line numbers.

---

## Security & Governance
- **Capabilities**: per‑request allowlist of SDK domains; deny by default.  
- **Policies**: CPU ms, memory MB, net hosts allowlist, path sandboxing for FS, call‑rate limits.  
- **Secrets**: injected via bindings, never surfaced to code; redact from logs.  
- **Audit**: persist code, SDK call ledger, timing, and outputs for review.  
- **Validation**: Zod/TypeBox at the binding boundary to defend against type‑erasure or bad inputs.

---

## Performance & Cost Heuristics
- **Fewer LLM passes**: multi‑step chains run **inside code** → often one LLM generation, one sandbox run.  
- **Smaller prompts**: stop enumerating dozens of tools; ship a short `.d.ts` instead.  
- **Cache**: memoize schema/SDK prompt chunk; cache transpilation; reuse isolates when safe.

---

## Interop with MCP (Optional)
- **Discovery**: connect to MCP servers for capability enumeration & auth.  
- **Façade generation**: transform MCP tool schemas into **TypeScript interfaces** and thin bindings for the sandbox.  
- **Runtime**: model still writes **code** against the TS façade; the sandbox binding calls the MCP server.  
- **When to prefer direct tools**: single tiny call with strict latency SLO; stepwise interactive UX.

---

## Migration Plan (from “many tools” → code‑mode)
1) **Inventory**: list tools; group into 3–7 domains; mark side‑effecting ops.  
2) **Design**: define `.d.ts` with narrow types; add JSDoc for intent and examples.  
3) **Runtime**: stand up the sandbox and bind only required domains.  
4) **Bridges**: add MCP→TS façades where needed; keep auth out‑of‑band and minimal.  
5) **Measure**: compare success rate, time‑to‑first‑answer, total tokens, SDK calls per task.  
6) **Tighten**: shrink SDK surface, add tests, refine prompts, tune limits.

---

## Testing & Reliability
- **Golden scenarios**: end‑to‑end tasks with expected code/output.  
- **Fuzz**: adversarial prompts containing non‑existent tools; assert the agent only calls the SDK.  
- **Chaos**: simulate timeouts, net failures, type errors; verify graceful fallbacks and user messaging.  
- **Telemetry**: tokens, exec_ms, sdk_calls, retries, success_rate.

---

## Example Policy (YAML)
```yaml
sandbox:
  cpu_ms: 1500
  memory_mb: 128
  net_access: allowlist
  fs_root: "/work/safe"
allow_sdk:
  - Memory
  - Files
  - HTTP
  - Out
validation:
  inputs: strict
  outputs: strict
retries:
  max_attempts: 1
logging:
  redact:
    - "apiKey"
metrics:
  - tokens_total
  - sdk_calls
  - exec_ms
  - success_rate
```

---

## Implementation Blueprint (choose one runtime)
1) **Cloudflare Workers / Isolates**: lightweight isolate per run; leverage Worker Loader to inject ephemeral modules; bind SDK via durable objects/dispatchers.  
2) **Deno Isolated Runtime**: run user code with permissions off; provide `Deno.core.ops` bound to SDK calls.  
3) **Node (VM/Isolate)**: hardened VM or isolates; disable `require`, `process`, timers unless explicitly needed; expose only SDK.

**Common components**
- **Transpiler**: swc/esbuild; inline source maps.  
- **Type packs**: `.d.ts` + markdown doc shipped to the model.  
- **Controller**: collects outputs/logs; enforces budgets; writes audit events.

---

## Risks & Mitigations
- **Runaway scripts** → CPU/mem/time quotas; AST scan to cap loop constructs; abort signals.  
- **Prompt injection** → never pass untrusted text directly to SDK; validate & encode; principle of least privilege.  
- **Secret leakage** → secret‑scoped bindings; redaction; deny writes of env to disk.  
- **Spec drift** (MCP/server changes) → regenerate façades; contract tests in CI.

---

## Summary
A **TS‑first code‑mode** surface concentrates power in a **small, typed SDK** and one execution path. It trims token and cognitive bloat, improves multi‑step reliability, and simplifies security. Use MCP for discovery/transport **when helpful**, but keep the model writing code—not juggling dozens of tools.

---

## Cloudflare Code Mode: Implementation Details

Below are concrete implementation notes distilled from Cloudflare’s Code Mode write-up and docs, adapted for this design.

### Enabling Code Mode in an app (AI SDK wrapper)
Use a helper that *wraps* your existing `system` and `tools` so the model emits small TS programs instead of direct tool calls. The runtime runs those programs in a sandbox and routes API calls through your bindings.

```ts
import { codemode } from "agents/codemode/ai";
import { streamText, openai } from "ai"; // or your LLM client

const { system, tools } = codemode({
  system: "You are a helpful assistant",
  tools: {
    // your existing tool definitions (MCP or non‑MCP)
  },
  // optional: config for sandbox/runtime limits, logging, etc.
});

const stream = streamText({
  model: openai("gpt-5"),
  system,
  tools,
  messages: [{ role: "user", content: "Write a function that adds two numbers" }],
});
```

**What changes:** your app now receives model‑authored **code** that calls the (typed) APIs; the runtime executes it and returns logs/results.

### MCP → TypeScript façade
When you connect to an MCP server in Code Mode, the Agents SDK fetches its schema and **generates a TS API** with doc comments based on the schema — effectively turning tool metadata into `interface`s and functions. Example shape (abbreviated):

```ts
interface SearchDocsInput { query: string; page?: number }
interface SearchDocsOutput { /* structured results */ }

export declare const codemode: {
  search_agents_documentation(input: SearchDocsInput): Promise<SearchDocsOutput>;
  // ...other generated methods
}
```

This façade is what your sandboxed code calls; under the hood, calls RPC back to the agent and out to the MCP server.

### Sandbox behavior
- **Single execution tool**: the agent exposes one tool that executes TS/JS.
- **No direct Internet**: the sandbox’s *only* egress is via the bound TypeScript APIs (your SDK and/or generated MCP façades).
- **Result channel**: the code returns results by writing to `console.log(...)`; when the script completes, logs are returned to the agent loop alongside a structured result envelope.
- **Auth keys are hidden**: bindings hold credentials; the AI‑written code never sees raw secrets. The supervisor injects tokens when dispatching to MCP servers.
- **On‑demand isolates**: Workers’ **Loader API** can load ephemeral code for each run (instead of shipping containers), keeping startup time and memory low.

### Why this helps
- **Composition inside code**: multi‑step workflows chain locally without extra LLM round‑trips.
- **Distribution match**: models have far more exposure to TS/JS than to synthetic tool‑call formats.
- **Governance**: one execution path, a small API surface, clear quotas and logs.

### Operational tips
- Keep the façade **tiny** (3–7 domains). Collapse overlapping tools.
- Validate inputs/outputs at the binding boundary (Zod/TypeBox).
- Log code, SDK calls, timings; surface redacted logs for debuggability.
- For organizations, centralize MCP connectivity/observability with portal tooling.

---

## References & Further Reading
- Cloudflare Blog: *Code Mode: the better way to use MCP*. https://blog.cloudflare.com/code-mode/
- Cloudflare Agents docs (overview). https://developers.cloudflare.com/agents/
- Agents configuration and API reference. https://developers.cloudflare.com/agents/api-reference/configuration/ , https://developers.cloudflare.com/agents/api-reference/agents-api/
- MCP in Agents docs. https://developers.cloudflare.com/agents/model-context-protocol/
- MCP Server Portals (security/observability). https://blog.cloudflare.com/zero-trust-mcp-server-portals/
- Original video commentary (source). https://www.youtube.com/watch?v=bAYZjVAodoo

