# Sylin.Koan.Classification.Contracts technical contract

## Boundary

The package contains only `ClassificationDataKey`, `IClassificationKeyProvider`, and the public custody/integrity
exceptions shared with Classification. It intentionally has no functional project reference, Koan module, discovery
hook, configuration binding, or default implementation.

## Provider obligations

- `GetActiveKey(scope)` returns the current key for the opaque scope supplied by Koan. Treat scope as an exact opaque
  identity; do not parse it or derive application meaning from it.
- `GetForDecrypt(keyId)` resolves every retained key id that may still appear in stored envelopes. Throw
  `ClassificationKeyUnavailableException` when custody cannot be recovered.
- Return an opaque stable key id and exactly 32 bytes of key material. Never log either key material or the scope.
- Own material lifetime, zeroing, durable storage, access control, rotation policy, and historical-key retention.
- Make concurrent calls safe. The functional runtime may share one provider across Entity types and operations.

Koan copies neither business segmentation values nor key material into its public facts. The functional runtime owns
AES-GCM, nonce generation, envelope serialization, scope compilation, and Data-pipeline placement.

## Non-goals

This contract does not define a KMS transport, vault configuration vocabulary, tenant API, shred/erasure operation,
or compliance policy. Provider packages can implement those concerns without forcing their functionality into this
inert interoperability boundary.
