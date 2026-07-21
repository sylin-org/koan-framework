# Sylin.Koan.Storage.Abstractions technical contract

## Boundary

This assembly is inert: no module, registration, configuration binding, provider selection, host state, or backend
client. Its Data Abstractions dependency exists only for `IStorageObject : IEntity<string>`.

## Contract groups

- `IStorageProvider` defines the mandatory byte lifecycle and requires a stable name, topology placement, and unified
  capability declaration.
- `StorageCaps` is the one declaration vocabulary used by routing, optional-operation guards, facts, and tests.
- `IStatOperations`, `IListOperations`, `IServerSideCopy`, and `IPresignOperations` are optional execution facets.
- `IStorageService` is the provider-neutral chokepoint implemented by the active Storage runtime.
- `StorageObject`, `IStorageObject`, `ObjectStat`, and `StorageObjectInfo` are cross-module result shapes.
- `[StorageBinding]` carries an Entity model's logical profile/container intent; contracts do not interpret it.

## Provider conformance

A provider's optional interface and matching capability token are an inseparable claim. At host composition Storage
rejects a token without its operation interface and an interface without its token. Capabilities describe what the
adapter can faithfully implement; endpoint readiness and credentials remain configuration/health concerns.

`StorageProviderPlacement.Local` participates as a cache/local tier; `Remote` participates as a durable/network tier.
`Composite` is reserved for an already-composed mechanism. Higher `[ProviderPriority]` wins automatic election within
one placement, with stable provider identity breaking ties. Exact application pins bypass automatic rank.

Provider packages own protocol clients, options, health, and physical IO. The Storage runtime owns profile semantics,
selection, replication composition, logical/physical identity, transfers, and evidence projection.
