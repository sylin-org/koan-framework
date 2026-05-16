---
id: LUMEN-0001
slug: LUMEN-0001-unified-thinking-pipeline
domain: ARCH
status: Accepted
date: 2026-02-21
---

# LUMEN-0001: Unified Thinking Pipeline — One Path, One Story

Date: 2026-02-21

Status: Accepted

**Scope clarification**: This ADR documents a thinking pipeline unification in the companion Koi project,
not in Koan Framework core. It is retained here for cross-project architectural reference.

## Context

When tools were enabled on the web channel, Lumen lost all conversation context.
She forgot her nickname, her personality, everything said two messages ago.

Root cause: two separate code paths —

- `ProcessAsync` — conversation-aware (memories, history, depth classification, prompt budget), single-shot, no tools.
- `ProcessWithToolsAsync` — tool-aware (multi-turn loop), but no conversation history, no memory retrieval, no depth classification.

A binary routing decision in `MessagePipeline` (`if profile.ToolsEnabled`) threw away the story when tools were enabled.

**Leon's insight**: every thinking act has a story. The distinction isn't "tools vs no tools" — it's that tools are capabilities layered on top of the story. A conversation's story is the exchange. An exploration's story is the curiosity thread. A musing's story is the spark. The summary of what happened is enough.

The two-path architecture existed because tool support was originally built for self-exploration (a stateless internal channel). When `ToolsEnabled` was flipped to `true` on the web channel, the tool path was used — but it had never been designed to carry conversation context.

## Decision

**One model client method. One pipeline method. No back-compat.**

### IModelClient — One way to talk to a model

`ChatAsync` is the only method. It accepts an optional `tools` list:

```csharp
Task<ModelResponse> ChatAsync(
    string systemPrompt,
    IReadOnlyList<ChatTurn> history,
    string model,
    IReadOnlyList<ToolDefinition>? tools = null,
    CancellationToken ct = default);
```

When tools are present, the response may contain tool calls. When absent, it's a plain text response. The old `ChatAsync(systemPrompt, userMessage, model, ct)` two-string overload is removed. All callers wrap their user message in a `ChatTurn` list.

`ToolAwareResponse` is deleted — `ModelResponse` absorbs its fields:

```csharp
public class ModelResponse
{
    public string Content { get; set; } = "";
    public string? ThinkingContent { get; set; }
    public List<ToolCallRequest>? ToolCalls { get; set; }
    public bool HasToolCalls => ToolCalls?.Count > 0;
    public int? TotalTokens { get; set; }
    public long? DurationMs { get; set; }
}
```

### OllamaModelClient — One implementation

Single `ChatAsync` method:
- Always builds the Ollama message list from the history.
- If `tools != null && tools.Count > 0`, include them in the request; otherwise the field is null (omitted from JSON via `WhenWritingNull`).
- Text responses always get `<think>` extraction and `CleanReasoningArtifacts`.
- Tool call responses pass content through raw (mid-loop self-talk).

### ThinkingPipeline — One ProcessAsync

```
ProcessAsync(message, conversation, channel, profile, ct):
  1. Wander detection (non-internal only)
  2. Classify thinking depth
  3. Build working memory snapshot (identity + memories)
  4. Identity validation (non-internal only)
  5. Compose system prompt (channel-aware)
  6. Compose the story as the first user ChatTurn:
     - Non-internal: ComposeThinkingContext (memories + conversation tail + compacted summary + current message)
     - Internal: just the seed message
  7. Select model, enforce prompt budget (non-internal only)
  8. Fetch tools if profile.ToolsEnabled (null otherwise)
  9. Multi-turn loop (max profile.MaxTurns):
     - Call ChatAsync(systemPrompt, history, model, tools)
     - If tool calls → execute via ToolRegistry, add results to history, continue
     - If [SYNTHESIS COMPLETE] → extract final content
     - If plain text → response, exit
  10. Unified diagnostics dump (story + tool traces when present)
  11. Return ThinkingResult
```

When tools are not enabled (or not called), the loop exits on turn 1 — single-shot behavior with zero overhead.

### MessagePipeline — No branching

```csharp
var profile = ChannelProfileRegistry.For(channel.Type);
var result = await _thinking.ProcessAsync(content, conversation, channel, profile, ct);
```

## Deleted Types and Methods

- `ToolAwareResponse` — absorbed into `ModelResponse`
- `ChatWithToolsAsync` on `IModelClient` — absorbed into `ChatAsync`
- `ChatAsync(string, string, string, CancellationToken)` overload — callers migrated
- `ProcessWithToolsAsync` on `ThinkingPipeline` — absorbed into `ProcessAsync`
- `DumpToolInteractionAsync` — merged into `DumpInteractionAsync`

## Files Modified

| File | Change |
|------|--------|
| `IModelClient.cs` | One `ChatAsync` signature, `ToolAwareResponse` deleted, `ModelResponse` extended |
| `OllamaModelClient.cs` | One `ChatAsync` impl, `<think>` + artifact cleaning on all text responses |
| `ThinkingPipeline.cs` | One `ProcessAsync`, merged diagnostic dump |
| `MessagePipeline.cs` | Remove if/else branching |
| 12 caller files | Migrate to `ChatAsync(systemPrompt, [ChatTurn], model, ct: ct)` |

## Consequences

**Positive:**
- Web channel + tools now carries full conversation context (the original bug fix).
- `<think>` traces are captured on all paths (previously missing from tool path).
- One diagnostic format (previously two: `_{depth}.md` and `_{depth}_tools.md`).
- Single-shot channels (reflection, musing) work identically — loop exits turn 1.
- No branching in MessagePipeline — profile drives behavior, not code paths.

**Negative:**
- All callers migrated from simple two-string pattern to explicit `ChatTurn` list.
  Slightly more verbose, but makes the contract explicit.

**Risks:**
- Multi-turn tool loop on web channel (MaxTurns=5) could produce longer latency
  if the model makes many tool calls. Mitigated by the existing turn limit.

## References

- Diagnostic file showing context loss: `.Koan/diagnostics/2026-02-21_23-20-52-319_Standard.md`
- ChannelProfile registry: `ChannelProfile.cs` — web: `ToolsEnabled=true, MaxTurns=5`
