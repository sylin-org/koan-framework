# Sylin.Koan.AI.Orchestration

Compose immutable, typed AI chains for chat, retrieval, parsing, branching, tools, and streaming.

```bash
dotnet add package Sylin.Koan.AI.Orchestration
```

## Meaningful use

Reference the package, retain `AddKoan()`, and execute from a running host:

```csharp
var result = await Chain.Create()
    .System("Answer only from retrieved articles.")
    .Retrieve<KnowledgeArticle>(question, topK: 5)
    .Chat("Question: {question}")
    .Run(new { question }, cancellationToken);

Console.WriteLine(result.Text);
```

Builders are immutable. Available decisions include `Parse<T>()`, `Classify(input, categories)`, named tuple branches
and parallel chains, `Rerank()`, `Compress()`, `Moderate()`, `WithTools(...)`, `WithMemory(...)`, provider `Scope(...)`,
and streamed `ChainChunk` output.

## Guarantees and limitations

- Reference plus `AddKoan()` automatically registers the DI-owned chain executor.
- Chat steps require a compatible AI provider. Retrieval requires Vector/embedding support for the selected Entity;
  unsupported filters fail rather than returning a misleading empty result.
- Steps execute in declared order and cancellation propagates. Branch/parallel/tool/provider failures remain visible.
- The chain is an in-process AI composition, not a durable workflow. It provides no retries, compensation,
  transactions, scheduling, human approval, or secret/tool authorization policy.

See [TECHNICAL.md](TECHNICAL.md) for the execution and ownership contract.
