# Koan.AI.Agents

Autonomous, entity-aware agents for Koan. Automatically generates tools from `Entity<T>` static methods, supports vector search, and uses ReAct reasoning to complete multi-step tasks.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Agents
```

## Quick Start

```csharp
var result = await Agent.Create()
    .System("You are a helpful assistant managing a product catalog.")
    .WithEntities<Product>()     // Auto-generates CRUD tools from Entity<T>
    .WithSearch<Product>()       // Adds semantic search tool
    .WithPlanning()              // Enables multi-step ReAct reasoning
    .Run("Find all products under $50 and summarise them", ct);

Console.WriteLine(result.Text);
```

## Entity Tools (auto-generated)

`WithEntities<T>()` reflects on `Entity<T>` static methods and generates callable tools automatically:

| Entity Method | Generated Tool |
|--------------|----------------|
| `T.Get(id)` | `get_{entity}` |
| `T.All()` | `list_{entity}` |
| `T.Query(expr)` | `query_{entity}` |
| `new T().Save()` | `create_{entity}` |
| `entity.Delete()` | `delete_{entity}` |

No manual tool definition required for standard CRUD operations.

## Fluent Builder API

```csharp
Agent.Create()
    .System(string systemPrompt)         // Persona / instructions
    .WithEntities<T>()                   // Auto CRUD tools for entity T
    .WithSearch<T>()                     // Semantic search tool for entity T
    .WithPlanning()                      // Enable multi-step planning (ReAct)
    .WithMemory()                        // Conversational history across turns
    .WithMaxIterations(int n)            // Default: 10
    .WithMaxTokens(int n)                // Token budget cap
    .Scope(IAiPipeline pipeline)         // Target a specific AI pipeline
    .Run(string message, CancellationToken ct)      // → AgentResult
    .Stream(string message, CancellationToken ct)   // → IAsyncEnumerable<AgentChunk>
```

## AgentResult

```csharp
public sealed class AgentResult
{
    string Text            { get; }  // Final response text
    AgentStatus Status     { get; }  // Completed | IterationLimitReached | BudgetExhausted
    IReadOnlyList<AgentStep> Steps { get; }  // Reasoning trace
    int    Iterations      { get; }
    int    TotalTokens     { get; }
    TimeSpan Duration      { get; }
}
```

## Streaming

```csharp
await foreach (var chunk in Agent.Create()
    .WithEntities<Product>()
    .Stream("List expensive products", ct))
{
    Console.Write(chunk.Content);
}
```

## Reference

- **ADR**: `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md`
- **Related**: `Koan.AI` (pipeline facade), `Koan.AI.Orchestration` (chain composition)
