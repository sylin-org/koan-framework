# AI-0007 — Inference servers interop (KServe, vLLM, TGI)

Status: Proposed
Date: 2025-08-19
Owners: Sora Web

## Context

Teams may standardize on KServe or OSS servers (vLLM, TGI) for model hosting. Sora should interoperate without coupling core behavior.

## Decision

- Provide interop guides for KServe (endpoint shape, auth, readiness) and OSS servers; add probe checks where safe.
- Consider optional adapters later; providers remain abstracted behind capability flags.

## Consequences

- Avoid server-specific code in core; keep examples and readiness checks in guides.
- Validate minimal sample calls and document headers/auth mapping.
