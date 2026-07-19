# Sylin.Koan.AI.Connector.HuggingFace technical contract

## Activation and contribution

Generated module activation binds `HuggingFaceOptions`, registers one client/adapter, and contributes the adapter to
the host's compiled AI provider plan as `huggingface`. The adapter declares only `AiCapability.ModelList` and
`AiCapability.Pull`; `ModelManager` owns search/acquisition operations.

## Hub and file behavior

The client uses the configured Hub base URL and bearer token when present. Model IDs use `owner/name`. Search maps Hub
metadata into `ModelEntry`; pull lists repository files, selects by explicit `ModelFormat` or the package's documented
preference order, downloads to a temporary file, and moves the completed file into the cache before catalog
registration. Cancellation flows through HTTP and file copy.

## Security and limits

The token comes from options or `HF_TOKEN` and is never a provenance value. The application owns secret delivery,
filesystem permissions/capacity, Hub terms/licenses, and artifact security review. The connector does not execute,
convert, scan, or validate model quality and does not provide offline mirroring or retry/rate-limit orchestration.
