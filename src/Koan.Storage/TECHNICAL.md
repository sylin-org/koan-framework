# Sylin.Koan.Storage technical contract

## Runtime ownership

`StorageModule` is the only supported registration path. It binds `Koan:Storage`, compiles one provider catalog and
one immutable routing plan, registers the physical-identity decorator, forces plan construction during module start,
and projects the same decisions into composition facts. Contract vocabulary lives in inert
`Sylin.Koan.Storage.Abstractions`.

## Provider catalog and routing plan

Core `ProviderCatalog<IStorageProvider>` owns normalized identities, priority metadata, stable ties, and duplicate
rejection. Storage specializes it with provider placement, unified `StorageCaps`, interface/capability conformance,
profile semantics, and corrections.

Every configured profile compiles to one `StorageRoute`: provider/composite instance, container, capability set, and
`ProviderSelectionReceipt`. Explicit pins are required intent. Automatic routes select the highest-priority candidate
within a placement. A single profile becomes the implicit default; multiple profiles without `DefaultProfile` remain
valid only for explicitly addressed operations.

Replication is a compiled composite owned and disposed by the routing plan. Explicit Replicated mode requires Local
and Remote candidates. Automatic mode composes them when both exist; one candidate remains a single-tier route.
Storage options are composition inputs and are not silently reinterpreted on each operation.

## Identity and segmentation

`ScopedStorageService` is the physical-identity chokepoint. It combines the immutable Storage identity plan with an
operation's Entity/host scope, applies guards, maps logical keys to physical keys, and maps results back without
changing Entity identity. Cross-cutting modules contribute generic segmentation; Storage contains no tenant branch.

## Operations and optional capabilities

`StorageService` is a thin executor over the compiled route. It requires the matching `StorageCaps` token before
presign/list operations, uses stat when declared, and attempts server-side copy only for the same provider with the
declared capability. Capability/interface disagreement is rejected while compiling the catalog.

Seekable uploads are hashed with SHA-256 while preserving caller position. Non-seekable uploads are buffered once so
they can be hashed and replayed; their reported size remains unknown (`0`) unless the contract changes. Cross-provider
copy reads then writes; move deletes only after target success.

Provider docs own actual streaming, consistency, range, stat, presign, durability, and atomicity guarantees. Storage
does not normalize a backend into stronger semantics or claim an atomic commit across provider bytes and Data rows.
