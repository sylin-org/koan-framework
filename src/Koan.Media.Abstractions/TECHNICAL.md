# Sylin.Koan.Media.Abstractions technical contract

## Boundary

This assembly owns portable Media vocabulary. It contains no `KoanModule`, dependency injection, configuration
binding, Entity implementation, ambient host access, controller, hosted worker, provider selection, decoder, or
encoder. Its project dependencies are the inert Data and Storage contract packages required by
`IMediaObject : IStorageObject : IEntity<string>`.

## Recipe invariants

- recipes are immutable after `Build()`;
- steps carry canonical pipeline stages independent of declaration order;
- `Fingerprint()` hashes version plus canonical steps and is the recipe half of derivative identity;
- `AllowedMutators` defines which request-time overrides a named recipe accepts; and
- `AllowedOutputFormats` is an allowlist, not an encoder promise—functional runtimes validate producibility.

## Pipeline contract

`IMediaPipeline` is lazy until `ProbeAsync`, `WriteToAsync`, `ToBytesAsync`, or `MaterializeAsync`. Prefer the
stream-writing terminal for production output. Implementations may require complete decoded pixel state even when
encoded output streams, so the functional ingress owner remains responsible for source bounds.

`IMediaRecipeRegistry`, `IOverlayResolver`, `MediaOutput`, `MediaBundle`, recipe steps, and media-kind metadata are
cross-module contracts. Runtime discovery, planning, execution, startup facts, and Entity-backed originals belong to
Media Core; request parsing, access-gated source resolution, negotiation, and HTTP diagnostics belong to Media Web.
