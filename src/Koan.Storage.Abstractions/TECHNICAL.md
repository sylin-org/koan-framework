# Sylin.Koan.Storage.Abstractions technical contract

## Boundary

This assembly is inert. It contains no `KoanModule`, service registration, provider selection, configuration binding,
ambient host access, or backend client. Its only project dependency is `Sylin.Koan.Data.Abstractions`, required by
`IStorageObject : IEntity<string>`.

## Contract groups

- `IStorageProvider` owns the required provider byte lifecycle: write, read, ranged read, delete, and existence.
- `IStorageService` is the provider-neutral orchestration seam exposed by the active Storage runtime.
- `IStatOperations`, `IListOperations`, `IServerSideCopy`, and `IPresignOperations` are optional provider facets.
- `StorageProviderCapabilities` is the compact capability declaration used during provider inspection.
- `StorageObject`, `IStorageObject`, `ObjectStat`, and `StorageObjectInfo` are cross-module result shapes.
- `StorageBindingAttribute` is application intent for logical profile/container placement; the runtime interprets it.

## Ownership rules

Provider packages may implement these contracts but do not place backend clients or options here. The Storage runtime
owns routing, identity/segmentation, replication, validation, transfer fallback, metadata enrichment, Entity helpers,
and startup reporting. Callers must not infer optional operations from provider names; they use the advertised
capabilities/interfaces and accept `NotSupportedException` when a requested guarantee is unavailable.

No type in this package activates Storage. That property is the reason this package exists as a separate reference
intent.
