---
type: PLAN
domain: ai
title: "Native provider function-calling for Koan.AI"
audience: [maintainers, framework-authors]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: reviewed
  scope: design grounded in the agents-rung dogfood (2026-08-24) and current Ollama adapter shape;
    slices 1-2 implemented and verified live the same day - see Implementation state below
---

# Native function calling — implementation plan

## Why

`AI.Agents` scrapes `​```tool_call` JSON blocks from model prose. Small local models — Koan's
primary audience — adhere poorly to ad-hoc text protocols: during validation, `qwen3:8b` answered
from priors while claiming it had no data access. The failure was invisible until execution logging
landed. Providers including Ollama support native tool calling (`message.tool_calls`), which removes
the adherence problem entirely for capable models and makes non-use detectable.

## Already landed (groundwork, additive)

- `Koan.AI.Contracts`: `AiToolDefinition`, `AiToolCall`, `ChatOptions.Tools`,
  `ChatResult.ToolCalls`. No behavior change; adapters may ignore.

## Implementation slices

1. **Ollama `/api/chat`**: the adapter only implements `/api/generate` (flat prompt). Add a chat
   path honoring `AiChatRequest.Messages` (roles preserved), mapping
   `ChatOptions.Tools → body.tools` (`{type:"function", function:{name,description,parameters}}`),
   and parsing `message.tool_calls` into `ChatResult.ToolCalls`.
2. **Executor preference**: `AgentExecutor` attaches registry definitions to `ChatOptions.Tools`;
   consumes native `ToolCalls` when present; falls back to `ParseToolCalls` text scraping when the
   response carries none (covers models without native support). Log which path fired.
3. **Other connectors** (LM Studio, Onnx-chat): follow-up rows; text fallback keeps them working.
4. **Verification**: agents-rung journey with `qwen3:8b` — assert `toolsCalled>=1` via native path,
   plus a model without native support still completing through the fallback.

## Non-goals

- Streaming tool calls; multi-provider parity certification; changing `ParseToolCalls` semantics.

## Implementation state (2026-08-24)

Slices 1, 2, and 4 landed; 3 remains open as follow-up rows.

- **Contract hop added**: `AiChatRequest.Tools` and `AiChatResponse.ToolCalls`, mapped by
  `Client.BuildChatRequest` and both `Client.ChatResult` overloads.
- **Adapter**: `OllamaAdapter.Chat` routes requests carrying tools through `/api/chat`
  (role-preserving messages, `tools` mapped from definitions, `message.tool_calls` parsed — argument
  object *and* string forms accepted); an HTTP refusal falls back to the legacy flat generate prompt,
  where the system text already carries any text-protocol instructions.
- **Second defect found during verification**: tool calls survived the adapter but died at the two
  Microsoft.Extensions.AI bridge hops — `ChatResponseMapper.FromAiChatResponse` /
  `ToAiChatResponse`. Both now translate between `AiToolCall` and `FunctionCallContent`.
- **Executor**: `BuildChatOptions` attaches registry definitions; `ResolveToolCalls` prefers native
  calls, logs which path fired per turn, and falls back to text scraping.
- **Verified live** via project-reference probe on qwen3:8b: five consecutive turns resolved via
  native function calling, grounded verbatim-title answer; dolphin-llama3:8b (refuses tools, HTTP
  400) completed through the fallback with the same journey. Unit specs pin the adapter wire shapes
  in `Koan.Tests.AI.Unit` (`OllamaAdapterSpec`).
