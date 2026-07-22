# Sylin.Koan.Data.Vector.Connector.InMemory

The supported zero-infrastructure vector floor for Koan: managed, process-local similarity search behind the ordinary Entity
vector ring.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.InMemory
```

This package brings the Vector runtime it implements. Keep the normal `AddKoan()` bootstrap; there is no provider
registration or configuration.

## Smallest meaningful result

```csharp
using Koan.Data.Core.Model;
using Koan.Data.Vector;

public sealed class Article : Entity<Article> { }

await Vector<Article>.Save("koan", [1f, 0f, 0f]);
var nearest = await Vector<Article>.Search([0.9f, 0.1f, 0f], topK: 5);
```

In-memory is Koan's lowest-priority automatic vector provider. A directly referenced durable provider supersedes it;
an explicit provider request remains exact and never silently falls back.

## What it supports

- cosine kNN over `System.Numerics.Tensors`;
- metadata filters through Koan's unified filter model;
- vector/keyword hybrid scoring;
- bulk upsert/delete, embedding retrieval, flush, export, and offset continuation;
- partition, routed-source, and tenant isolation through the same Vector naming and scope pipeline as other providers.

## Operational contract

Data lives only in the current process and disappears when that process stops. The provider has no external readiness
dependency and performs no filesystem or network I/O. Startup facts identify it as the ephemeral fallback floor.

## Boundaries

Use it for a first meaningful semantic result, tests, local workflows, and bounded single-process datasets. Do not use
it when restart durability, multi-process sharing, distributed indexing, approximate-nearest-neighbour scale, or a
backend-specific consistency guarantee is required.

## References

- [Technical reference](./TECHNICAL.md)
- [Vector runtime](../../../../Koan.Data.Vector/README.md)
