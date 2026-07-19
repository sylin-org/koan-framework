# Sylin.Koan.AI.Agents

Run bounded, entity-aware AI agents with immutable configuration and read-only Entity tools by default.

```bash
dotnet add package Sylin.Koan.AI.Agents
```

## Meaningful use

Reference the package, keep the host's existing `AddKoan()`, then run from an active host:

```csharp
var result = await Agent.Create()
    .System("Answer from the product catalog.")
    .WithEntities<Product>()
    .WithSearch<Product>()
    .WithMaxIterations(6)
    .Run("Which products are suitable for a small studio?", ct: cancellationToken);

Console.WriteLine(result.Text);
```

`WithEntities<T>()` exposes read tools. Pass `write: true` only when the application deliberately permits model-driven
create/update/delete operations. `WithSearch<T>()` adds semantic retrieval. `WithTools(...)`, `WithMemory(...)`,
`Scope(...)`, token limits, and streamed `AgentStep` output are explicit choices.

## Guarantees and limitations

- Reference plus `AddKoan()` automatically registers the DI-owned executor; there is no Agents registration call.
- Execution requires a running Koan host, a chat-capable AI provider, and the Data providers used by selected Entity
  tools. Semantic search also requires the Entity's Vector/embedding path.
- Iteration and token limits bound planning, not external tool latency or provider cost. Tool handlers must remain
  authorized, idempotent where replay matters, and safe for untrusted model arguments.
- An absent host/executor or provider fails with a correction. Tool, AI, and Entity errors remain failures; the agent
  does not invent fallback data or transactions.
- This package does not provide durable workflow orchestration, human approval, sandboxing, scheduling, or cross-tool
  atomicity. Use Jobs, Review, and application authorization at their own boundaries.

See [TECHNICAL.md](TECHNICAL.md) for activation, execution, tool generation, and failure ownership.
