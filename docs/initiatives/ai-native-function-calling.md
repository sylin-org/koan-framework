---
type: PLAN
domain: ai
title: "Native provider function-calling for Koan.AI"
audience: [maintainers, framework-authors]
status: proposed
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: reviewed
  scope: design grounded in the agents-rung dogfood (2026-08-24) and current Ollama adapter shape
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
