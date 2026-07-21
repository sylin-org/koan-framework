# Sylin.Koan.Data.Vector.Connector.Weaviate

Use Weaviate behind Koan's entity-first vector API. Referencing this package activates the adapter; `AddKoan()` owns
registration, provider election, schema naming, health participation, and startup reporting.

## Install and use

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Weaviate
```

```csharp
public sealed class Article : Entity<Article> { }

await Vector<Article>.Save(article.Id, embedding, new { category = "support" });
var matches = await Vector<Article>.Search(embedding, topK: 12);
```

No Weaviate-specific registration is required. With Weaviate at `http://localhost:8080`, no configuration is required.
Set `Koan:Data:Weaviate:Endpoint` for another deployment and `ApiKey` when authentication is enabled.

Weaviate derives dimension from the first embedding and rejects later dimension changes for that entity collection.
Koan defaults `topK` to 10; an explicit positive value is sent unchanged.

## Honest capability boundary

Weaviate supports KNN and hybrid text/vector search, native search continuation, metadata filtering, bulk operations,
embedding reads, export, collection clear, and dynamic collection naming. Its filter operator set is deliberately
declared and smaller than Koan's full AST (notably no `In`); unsupported predicates fail before I/O instead of silently
broadening a query.

When `Koan.ZenGarden` is also referenced and active, its Weaviate offering becomes one health-checked discovery
candidate. Without that engine the capability remains inert. An explicit native endpoint always wins.

See [TECHNICAL.md](./TECHNICAL.md) for layered discovery, naming, health, and failure behavior.
