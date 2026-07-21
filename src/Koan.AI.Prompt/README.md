# Sylin.Koan.AI.Prompt

Optional Entity-backed storage for named, versioned Koan AI prompts. The in-memory `Prompt` value belongs to
`Sylin.Koan.AI.Contracts` and is available through the normal AI runtime; reference this package only when prompts
must be edited or selected from persisted application data.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Prompt
```

The package brings Koan Data because `PromptEntry` is an Entity. Add one Data provider appropriate to the application.

## Smallest meaningful use

```csharp
using Koan.AI.Prompt;
using Koan.Data.Core;

await new PromptEntry
{
    Name = "order-summary",
    Version = 1,
    Status = PromptStatus.Active,
    Content = "Summarize order {orderId} in one sentence."
}.Save();

var prompt = await PromptCatalog.Load("order-summary");
var answer = await Client.Chat(prompt, new { orderId = order.Id });
```

`PromptCatalog.Load(name)` returns the highest active version. `PromptCatalog.Load(name, version)` returns that exact
version, including draft or retired entries, so deliberate rollback and review tools can inspect historical content.

## Guarantees and boundaries

- Names are ordinary application data; Koan does not create, seed, or reserve them.
- A missing active or exact version throws `PromptNotFoundException` with the requested identity.
- Duplicate Entities with the same name/version fail correctively instead of selecting an arbitrary record.
- This package stores content, constraints, lifecycle state, authorship, notes, and tags through normal Entity
  semantics. The selected Data provider owns durability, concurrency, and transaction guarantees.
- Koan does not provide random A/B assignment, canary stickiness, prompt approval workflow, encryption, or a prompt
  administration UI in this package.
- Referencing `Sylin.Koan.AI` alone does not activate Data or this catalog.

See [TECHNICAL.md](./TECHNICAL.md) for ownership and lookup behavior.
