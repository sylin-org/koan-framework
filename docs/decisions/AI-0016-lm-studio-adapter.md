# AI-0016 - LM Studio adapter for Koan AI

Status: Accepted
Date: 2025-10-19
Owners: Koan AI

## Context

Koan already supports local inference through the Ollama adapter and remote providers via OpenAI-compatible shims. Teams increasingly use LM Studio to host local gguf models with an OpenAI API surface (chat completions, embeddings, models). The absence of a first-party LM Studio adapter forces developers to craft ad-hoc HTTP calls, bypassing Koan's routing, readiness, and discovery pipelines. We need parity with Ollama for DX consistency and to support demos that rely on LM Studio's desktop or headless server.

## Decision

- Add `Koan.AI.Connector.LMStudio` targeting net10.0, implementing `IAiAdapter`, readiness contracts, streaming, embeddings, and model listing.
- Provide autonomous discovery (`LMStudioDiscoveryAdapter`) honoring environment overrides, Aspire bindings, host-first loopback, and container fallbacks.
- Expose typed options (`LMStudioOptions`) with auto-discovery defaults, API key, default model, and readiness policy.
- Register components through a Koan auto-registrar: options configurator, discovery adapter, orchestration evaluator, and AI router member publication.
- Publish connector documentation (`README.md`, `TECHNICAL.md`) and unit tests validating chat serialization, streaming SSE consumption, embeddings, and readiness states.

## Consequences

- Koan AI router gains a first-class LM Studio provider, selectable via adapter id `lmstudio` and labels from configuration.
- Readiness gating matches Ollama semantics: `/v1/models` probing and default-model verification degrade readiness when the model is absent.
- Orchestration metadata describes port 1234, `/health` probe, and persistent volume expectations, enabling container provisioning flows.
- Developers configure LM Studio endpoints via environment variables (`LMSTUDIO_API_BASE_URL`, `Koan_AI_LMSTUDIO_URLS`) or app settings without writing custom boot code.
- Tests cover serialization and SSE parsing, reducing regression risk when refactoring request/response models.

## References

- AI-0008 AI adapters and registry
- AI-0005 Protocol surfaces (OpenAI compatibility)
- AI-0004 Secrets provider
- AI-0015 Canonical source member architecture
