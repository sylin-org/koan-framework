# Sylin.Koan.Classification technical contract

## Composition owner

`ClassificationModule` registers one cipher, a lowest-ceremony Development key provider, and one
`IFieldTransformContributor`. Data Core owns the contributor contract and compiles one immutable, type-memoized
transform plan per host. Classification does not mutate a process-static registry and the hot path does not resolve
services through `AppHost.Current`.

The package reference is intent. `AddKoan()` discovers the module; there is no `AddClassification()` call.

## Write and read chokepoints

For an Entity type with classified properties, the compiled plan clones the Entity and applies transforms in
contributor order before Data calls the selected repository. On materialization, it applies transforms in reverse
order. The original write instance remains plaintext.

`ClassifiedPropertyBag` compiles property getters and setters once. A classified property must be a writable
`string`; plan construction rejects unsupported shapes. Null values remain null. A value carrying the reserved
`kfe1:` prefix must be a valid envelope; malformed protected data fails closed. Plaintext without the prefix remains
readable and is encrypted on its next supported write.

The envelope carries only version marker, opaque key id, nonce, authentication tag, and ciphertext. AES-GCM uses a
96-bit nonce and 128-bit tag with a 256-bit key.

## Key scope and custody

The contributor receives Core's host-owned `SegmentationPlan` and compiles the scope applicable to each Entity type.
At write time it binds active hard dimensions once, length-prefixes dimension ids and values, and hashes the result
to an opaque `seg:` scope. With no applicable dimensions it uses `host`. Raw segmentation values never enter the
envelope, key id, logs, or runtime facts.

The runtime asks `IClassificationKeyProvider.GetActiveKey(scope)` only when a non-null plaintext field needs
encryption. Reads resolve the envelope's key id with `GetForDecrypt(keyId)`; they do not rely on the caller's current
scope, which permits retained keys to decrypt data after rotation.

`EphemeralClassificationKeyProvider` retains per-scope active and historical keys in process memory, rotates by
encryption count, and zeroes retained material on disposal. Module start rejects it outside Development. A custom
provider registered before `AddKoan()` replaces the default through standard DI `TryAdd` precedence.

## Cache and observability

Data exposes `IFieldTransformInspector`; Cache consumes that neutral contract and excludes Entity types with active
field transforms. Cache does not reference Classification and Classification does not configure Cache.

The Data axis includes a `field-transform` explanation plane containing contributor ids. Startup logs the selected
provider type without key ids, scopes, or values. Composition facts publish the capability and mechanics, not secret
material.

## Dependencies and exclusions

The functional package depends on `Sylin.Koan.Classification.Contracts`, Core, Data abstractions, and Data Core. The
contracts package has no functional dependency and cannot activate Classification by itself.

Current exclusions are searchable ciphertext, blind indexing, tokenization, caller masking, non-Data redaction,
automatic backfill, erasure/shred workflows, production key custody, and direct-provider interception. Authentication
of stored envelopes detects tampering; it does not protect values that never pass through the supported Data facade.
