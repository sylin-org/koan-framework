# Sylin.Koan.Storage technical contract

## Runtime ownership

`StorageModule` binds `Koan:Storage`, registers one `IStorageService`, validates profiles, and reports effective
configuration. Provider/service/object vocabulary lives in inert `Sylin.Koan.Storage.Abstractions`; this package owns
functional routing, physical identity, segmentation, replication, transfer fallback, and Entity helpers.

## Routing and validation

Profiles map a logical name to provider, container, and optional mode. Explicit provider/profile choices are exact.
When the provider is omitted, the runtime classifies registered local/remote providers and resolves Local, Remote, or
Replicated mode. `ValidateOnStart` rejects empty profiles, missing containers, unknown explicit providers, and invalid
defaults. `SingleProfileOnly` is the bounded implicit fallback; it never resolves among several profiles.

## Identity and segmentation

`ScopedStorageService` is the physical-identity chokepoint. It composes the immutable Storage identity plan with the
operation's Entity/host scope, runs storage guards, maps logical keys to physical keys, and projects results back
without leaking composed keys into Entity identity. Cross-cutting modules contribute generic segmentation; Storage
does not contain tenant-specific branches.

## Transfers and optional capabilities

`StorageService` uses `IServerSideCopy` only for the same provider; otherwise it streams source to target and deletes
the source only after a successful write when move was requested. `IStatOperations`, `IListOperations`, and
`IPresignOperations` remain optional and fail honestly when unavailable. A best-effort stat fallback may open the
object and can return unknown length/content metadata.

## Resource and consistency posture

Seekable uploads are hashed with SHA-256 while preserving the caller stream position. Non-seekable uploads are
buffered once so they can be hashed and replayed to a provider; their reported size remains unknown (`0`) unless the
provider can establish it. Storage does not claim an atomic commit across provider bytes and Data Entity rows.
Replication/cache consistency is governed by the selected mode and provider behavior, not normalized into a stronger
framework guarantee.
