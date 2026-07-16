# Koan.Media.Abstractions — technical contract

## Ownership

This package owns portable Media types. It contains no controller, hosted worker, provider election, or
image engine. `MediaEntity<TEntity>` composes Data and Storage contracts; Core supplies execution and Web
supplies negotiated HTTP rendering.

## Recipe invariants

- recipes are immutable after `Build()`;
- steps execute in canonical stage order, independent of declaration order;
- `Fingerprint()` hashes version plus canonical steps and is the recipe half of derivative identity;
- `AllowedMutators` defines which request-time overrides a named recipe accepts;
- `AllowedOutputFormats` is an allowlist, not an encoder promise; Core rejects unproducible formats.

## Resource posture

The pipeline contract supports stream-writing terminals, but image decoding may still require full decoded
pixel state. `MediaEntity.Store(Stream, ...)` currently buffers the source. Callers must enforce appropriate
upload and decode bounds at their ingress.

## Compatibility boundary

The package is pre-1.0. There is no eager/prewarm flag: no supported upload-time execution contract exists.
Future lifecycle or Entity-facet APIs require a real coordinator and consumer evidence rather than symmetry
with other pillars.
