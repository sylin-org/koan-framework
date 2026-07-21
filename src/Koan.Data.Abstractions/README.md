# Sylin.Koan.Data.Abstractions

Provider-neutral contracts shared by Koan's Entity runtime, data connectors, and modules that need to negotiate data
behavior without activating a provider or host.

Applications normally receive this package through `Sylin.Koan` or `Sylin.Koan.App`. Reference it directly when
implementing a repository/adapter, declaring data capabilities, or consuming the contracts without Data Core.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Abstractions
```

## Meaningful use

An adapter implements `IDataRepository<TEntity,TKey>` and, when applicable, `IQueryRepository<TEntity,TKey>`.
It advertises only behavior it proves through the shared capability model:

```csharp
var capabilities = DataCaps.Describe(repository, repository.GetType().Name);
var canStreamByPages = capabilities.Has(DataCaps.Query.ProviderBoundedPaging);
```

`QueryDefinition` carries the structured candidate filter, sort, projection, page, partition, and count intent.
`RepositoryQueryResult<TEntity>` reports which axes the provider actually handled. Consumers negotiate those facts;
they do not infer guarantees from an adapter name.

The package also owns the canonical provider-neutral `PatchPayload<TKey>` operation. HTTP JSON Patch, Merge Patch,
MCP, or another projection must normalize its protocol into that operation before entering Data.

## Boundaries and failures

- This package is inert vocabulary. It does not register Data Core, elect a provider, open storage, create schemas,
  or expose `Entity<T>` statics.
- It has no ASP.NET Core dependency. Media types, JSON Patch documents, controllers, and HTTP errors belong to Web.
- `ProviderBoundedPaging` promises bounded candidate pages and a complete order, not cursor resumption, snapshot
  isolation, or mutation-safe traversal.
- Unsupported optional behavior must reject or negotiate honestly; adapters must not approximate a stronger
  guarantee silently.
- A capability token is a claim, not proof. Provider conformance tests must exercise every advertised behavior.
- Provider exceptions and correction detail remain adapter-owned; the contract does not erase them behind a generic
  wrapper.

See [TECHNICAL.md](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.Data.Abstractions/TECHNICAL.md)
for query ownership, capability semantics, and adapter compatibility rules.
