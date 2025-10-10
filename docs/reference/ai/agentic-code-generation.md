---
type: REFERENCE
domain: ai
title: "Agentic AI Code Generation Reference"
audience: [developers, ai-engineers, ai-agents]
last_updated: 2025-02-15
framework_version: v0.6.3
status: current
validation: 2025-02-15
replaces: []
---

# Agentic AI Code Generation Reference

**Document Type**: REFERENCE
**Target Audience**: Developers, AI Engineers, Agent Authors
**Last Updated**: 2025-02-15
**Framework Version**: v0.2.18+

---

## Overview

This reference explains how to build agent-ready code generation workflows on Koan. It focuses on AI orchestrations that
produce runnable C# (or configuration) snippets, persist generated artifacts, and expose identical behaviour through REST and
MCP so autonomous agents can extend services safely.

## Prerequisites

- Koan v0.2.18 or later with the AI and Web pillars enabled
- At least one configured AI provider (OpenAI, Azure OpenAI, or local inference via Ollama)
- Familiarity with `IAi.ChatAsync`, `IAi.EmbedAsync`, and `EndpointToolExecutor`
- MCP transport enabled via `AddKoanMcp()` when exposing tools to IDE/CLI agents

## Core Concepts

### 1. Structured Prompting for Code Generation

Use system prompts that declare file targets, safety policies, and validation steps. Koan’s AI Engine can project responses into
JSON or Markdown for deterministic parsing.

```csharp
public static class CodeGenPrompts
{
    public static AiChatRequest CreateCodeGenRequest(string instructions, string? schema = null)
    {
        var systemPrompt = "You are a Koan agent that emits compilable C# snippets with explanations and validation hints.";

        return new AiChatRequest
        {
            Model = AiModels.Default,
            Messages =
            [
                new() { Role = AiMessageRole.System, Content = systemPrompt },
                new() { Role = AiMessageRole.User, Content = instructions }
            ],
            ResponseFormat = schema is null
                ? AiChatResponseFormat.Markdown
                : AiChatResponseFormat.JsonSchema(schema)
        };
    }
}
```

### 2. Vector-Augmented Retrieval for Context

Store project scaffolding, API contracts, or ADR excerpts as embeddings so agents ground generation in authoritative sources.

```csharp
public class AgenticSnippetContext
{
    private readonly IAi _ai;

    public AgenticSnippetContext(IAi ai) => _ai = ai;

    public async Task<float[]> EmbedDocumentAsync(string content)
    {
        var result = await _ai.EmbedAsync(new AiEmbeddingRequest
        {
            Input = content,
            Model = AiModels.Embeddings.Default
        });

        return result.Embeddings.FirstOrDefault()?.Vector ?? [];
    }

    public Task<AgentSnippet[]> SearchAsync(string query) =>
        Vector<AgentSnippet>.SearchAsync(query, limit: 8);
}
```

### 3. Guardrails and Validation Hooks

Generated code should pass through validation before execution. Wrap persistence in entity hooks or service methods that run
syntax checks, unit tests, or policy filters.

```csharp
public class GeneratedRoutine : Entity<GeneratedRoutine>
{
    public string Title { get; set; } = string.Empty;
    public string Language { get; set; } = "csharp";
    public string Source { get; set; } = string.Empty;
    public string[]? Diagnostics { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public override async Task OnSavingAsync(EntitySaveContext context)
    {
        var validator = context.Services.GetRequiredService<ICodeValidationService>();
        var results = await validator.ValidateAsync(Language, Source);

        if (results.HasErrors)
        {
            Diagnostics = results.Errors;
            throw new InvalidOperationException("Generated code failed validation");
        }

        Diagnostics = results.Warnings;
    }
}
```

## Implementation

### REST Endpoint Pattern

Expose a controller method that orchestrates retrieval, prompting, validation, and persistence. Surface rate-limit headers so
human and agentic clients can inspect costs.

```csharp
[Route("api/[controller]")]
public class CodeGenController : EntityController<GeneratedRoutine>
{
    private readonly IAi _ai;
    private readonly AgenticSnippetContext _context;

    public CodeGenController(IAi ai, AgenticSnippetContext context)
    {
        _ai = ai;
        _context = context;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<GeneratedRoutine>> Generate([FromBody] CodeGenRequest request, CancellationToken ct)
    {
        var groundingSnippets = await _context.SearchAsync(request.Query);
        var schema = CodeSchemaCatalog.Lookup(request.Language);

        var instructions = PromptBuilder.ForRoutine(request, groundingSnippets);
        var aiRequest = CodeGenPrompts.CreateCodeGenRequest(instructions, schema);

        var response = await _ai.ChatAsync(aiRequest, ct);
        Response.Headers.ApplyAiDiagnostics(response.Diagnostics);

        var code = ResponseParser.ExtractCode(response);
        var routine = new GeneratedRoutine
        {
            Title = request.Title,
            Language = request.Language,
            Source = code,
            Diagnostics = response.Diagnostics?.Select(d => d.Message).ToArray()
        };

        await routine.Save(ct);
        return CreatedAtGet(routine.Id!, routine);
    }
}
```

### MCP Tool Wrapper

Use `EndpointToolExecutor` so MCP clients (e.g., IDE copilots) invoke the same endpoint without duplicating orchestration.

```csharp
[McpEntity("generated-routine", AllowMutations = true, RequiredScopes = ["codegen:write"])]
public class GeneratedRoutine : Entity<GeneratedRoutine> { /* ... */ }

public class CodeGenTool : IMcpTool
{
    private readonly EndpointToolExecutor _executor;

    public CodeGenTool(EndpointToolExecutor executor) => _executor = executor;

    public string Name => "code-generation.generate";

    public Task<McpResult> InvokeAsync(McpInvocationContext context, CancellationToken cancellationToken)
        => _executor.ExecuteAsync("GeneratedRoutine", "Generate", context, cancellationToken);
}
```

### Background Review Workflow

Schedule follow-up validation or notification via Koan’s scheduling pillar once code is persisted.

```csharp
public class CodeReviewJob : IRecurringJob
{
    public string Name => "agentic.codegen.review";
    public TimeSpan Schedule => TimeSpan.FromMinutes(10);

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken ct)
    {
        var pending = await GeneratedRoutine.Query()
            .Where(r => r.Diagnostics != null && r.Diagnostics.Length > 0)
            .ToArrayAsync(ct);

        var messenger = services.GetRequiredService<IMessenger>();
        foreach (var routine in pending)
        {
            await messenger.PublishAsync(new CodeGenReviewRequested(routine.Id!, routine.Diagnostics!), ct);
        }
    }
}
```

## Configuration

```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "openai",
      "Providers": {
        "openai": {
          "Type": "OpenAi",
          "ApiKey": "${OPENAI_API_KEY}",
          "DefaultModel": "gpt-4.1-mini",
          "Budget": {
            "DailyUsd": 25.0,
            "OnOverBudget": "Throttle"
          }
        },
        "ollama": {
          "Type": "Ollama",
          "Host": "http://localhost:11434",
          "DefaultModel": "codellama"
        }
      }
    },
    "Mcp": {
      "Transport": {
        "EnableStdio": true,
        "HeartbeatSeconds": 30
      }
    }
  }
}
```

## Common Patterns

- **Schema-constrained responses** keep generated files parseable; use `AiChatResponseFormat.JsonSchema` when agents must write
  manifest files.
- **Dual-surface diagnostics** ensure REST and MCP clients share the same warnings; always propagate `AiDiagnostics` headers via
  `ResponseTranslator`.
- **Scoped mutations** protect repositories; restrict MCP write access with `RequiredScopes` and verify inside entity hooks.
- **Replayable prompts** persist prompt + context in `GeneratedRoutine` so humans can review or rerun with improved grounding.

## Troubleshooting

| Issue | Resolution |
| --- | --- |
| Responses omit code fences | Force Markdown format and run a post-processor that extracts fenced blocks before validation. |
| Validation service rejects generated code | Surface diagnostics through HTTP headers and MCP tool responses; include the
failing snippet so agents can patch it incrementally. |
| MCP tool not discoverable | Confirm entity is decorated with `McpEntityAttribute`, transport is enabled, and the server
advertises the tool via health announcements. |
| Budget throttling interrupts generation | Lower `MaxOutputTokens`, switch to local inference provider, or reschedule heavy
jobs to background processors with relaxed budgets. |

## Related Documentation

- [AI Pillar Reference](index.md)
- [AI Integration Guide](../../guides/ai-integration.md)
- [AI-0013 - S12.MedTrials sample adoption](../../decisions/AI-0013-s12-medtrials-sample.md)
- [S12.MedTrials Proposal](../../archive/proposals/s12-medtrials-sample-proposal.md)

## Validation Checklist

- [x] Code examples compile against Koan abstractions
- [x] Configuration schema validated against sample appsettings
- [x] AI request/response patterns align with AI Engine contracts
- [x] MCP integration reuses `EndpointToolExecutor`
- [x] Links resolve within repository

---

**Last Validation**: 2025-02-15 by Koan Samples Guild
**Framework Version Tested**: v0.2.18+
