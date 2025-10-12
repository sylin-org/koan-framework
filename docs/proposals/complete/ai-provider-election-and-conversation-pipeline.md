# AI Provider Election & Conversation Pipeline Proposal

**Status**: Implemented
**Implementation Evidence**: AI adapter election metadata, the conversation builder, and streaming updates are available under `src/Koan.AI` and its contracts as described.
**Author**: Platform Architecture  
**Date**: 2024-11-05  
**Version**: 0.1

## Executive Summary

Koan's AI surface currently requires manual provider hints and exposes a minimal prompt facade that does not reflect the augmentation-first workflow described in ADR AI-0010. This proposal unifies two overdue investments—automatic provider election and a conversation-centric developer experience—into a single roadmap. By pairing policy-driven adapter prioritization with a fluent conversation builder and augmentation pipeline, Koan can deliver "just works" defaults comparable to Koan.Data while giving teams a Hugging Face/Ollama-style API that scales from simple prompts to orchestrated multi-turn scenarios.

## Problem Statement

### 1. Manual AI adapter selection breaks Koan's plug-and-play promise
- `InMemoryAdapterRegistry` preserves registration order rather than adapter desirability.
- `DefaultAiRouter` round-robins or requires explicit route hints, so whichever provider registers first tends to win, regardless of health or capability.
- `AiOptions.DefaultPolicy` is unused, making it impossible to express fleet-wide preferences (e.g., "prefer managed inference over local dev instances").

### 2. Prompt-centric APIs block richer DX patterns
- The AI contracts transport flat `{ role, content }` arrays with few metadata hooks, forcing augmentations (moderation, RAG, budgeting) into vendor bags.
- `Ai.Prompt` one-liners lack discoverability for conversation state, tool calls, or profiles, diverging from ADR AI-0010 and common Hugging Face/Ollama experiences.
- Streaming responses emit plain text frames instead of structured SSE events, limiting telemetry, tool, and citation delivery.

## Goals & Non-Goals

**Goals**
- Deliver automatic provider election that mirrors `ProviderPriorityAttribute` in Koan.Data.
- Introduce a conversation builder that encapsulates context, augmentation opt-ins, and streaming metadata.
- Maintain backward compatibility for existing `Ai.Prompt` and HTTP consumers via shims.

**Non-Goals**
- Building every augmentation (RAG, moderation) in this phase; we provide hooks, not complete feature sets.
- Implementing a Hugging Face adapter; the proposal prepares the platform but does not add provider-specific projects.

## Proposed Solution

### A. Policy-driven adapter election
1. **Election Metadata**
   - Extend `IAiAdapter` (or introduce a companion attribute) with priority metadata: `ElectionWeight`, `DefaultProfiles`, `PreferredPolicies`.
   - Mirror the `ProviderPriorityAttribute` pattern so adapters can declare intent without runtime configuration.
2. **Registry Ordering**
   - Upgrade `InMemoryAdapterRegistry` to store adapters with metadata, exposing sorted views per capability (chat, streaming, embeddings).
   - Cache evaluation results to avoid recomputing priority across requests.
3. **Router Policies**
   - Teach `DefaultAiRouter` to evaluate adapters against `AiOptions.DefaultPolicy`, health checks, and capability fit before selection.
   - Provide built-in policies (e.g., `PreferManaged`, `PreferLocal`, `HealthAware`, `LatencyAware`) configurable per profile.
4. **Testing & Telemetry**
   - Add unit tests covering deterministic selection with mixed priorities and policy overrides.
   - Emit structured boot logs ("AI: route chat.default → hugging-face (priority 80)") for observability.

### B. Conversation-centric developer experience
1. **Conversation Contracts**
   - Introduce `AiConversationRequest` with explicit roles (system, user, assistant, tool), attachments, and context metadata (profile, budget, augmentation flags).
   - Provide shims that translate legacy `AiChatRequest` into the new model to preserve compatibility.
2. **Fluent Builder & Pipeline**
   - Add `Ai.Conversation()` builder with methods like `.WithProfile("support")`, `.UseAugmentation("moderation")`, `.AddToolCall(...)`, `.Ask("...", stream: true)`.
   - Implement an augmentation pipeline with stages (prepare → pre-call → provider → stream → finalize) allowing modules to register enrichers.
3. **Streaming Modernization**
   - Refactor SSE endpoints to emit JSON envelopes (`event: chunk`, `data: { delta, model, adapterId, usage, annotations }`) aligned with ADR AI-0002.
   - Support tool/citation and telemetry events alongside text chunks.
4. **DX Safeguards**
   - Update documentation, samples, and templates to highlight the new flow.
   - Keep `Ai.Prompt`/`Ai.Stream` as thin wrappers around the builder, easing migration.

## Implementation Plan

| Phase | Duration | Scope | Key Deliverables |
|-------|----------|-------|------------------|
| 1. Metadata Foundations | 1 sprint | Define adapter election metadata, registry ordering, router policy hooks | Updated contracts, registry, and router tests |
| 2. Conversation Contracts | 1 sprint | Introduce `AiConversationRequest`, compatibility shims, and DTO updates | Contract classes, mapper utilities, initial docs |
| 3. Builder & Pipeline | 1-2 sprints | Build conversation builder, augmentation pipeline, and adapter integration | Fluent API, augmentation interfaces, sample augmenters |
| 4. Streaming & HTTP Updates | 1 sprint | Emit structured SSE, update controllers and clients | JSON SSE responses, integration tests, docs |
| 5. DX Polish | ongoing | Update documentation, templates, telemetry, and boot reporting | Guides, migration notes, sample updates |

## Impact Assessment

- **Developer Experience**: Auto-elected providers and fluent conversations reduce boilerplate and align with Hugging Face/Ollama expectations.
- **Operational Readiness**: Router policies support fleet management (health, cost, latency) without manual code changes.
- **Backward Compatibility**: Shims keep existing APIs functional; adapters opt into metadata without breaking changes.
- **Extensibility**: Augmentation pipeline unlocks future work (RAG, moderation, tool calling) without redesigning the surface again.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Adapter metadata drift or misconfiguration | Provide sensible defaults, validation, and boot diagnostics highlighting priority collisions. |
| Breaking legacy clients during contract transition | Maintain old DTOs, add compatibility controller endpoints, document upgrade path. |
| Increased complexity in router | Keep policies composable and well-tested; expose diagnostics for decision traces. |
| Streaming format changes impacting consumers | Offer opt-in headers for the new SSE format during transition; document examples and client helpers. |

## Open Questions

1. Should election metadata live on adapters (attribute) or registration (options) to support environment-specific overrides?
2. How should we represent hedging/failover across multiple providers within a single conversation request?
3. Which augmentations should ship as first-party examples (e.g., moderation, telemetry) to validate the pipeline?
4. Can we surface policy decisions in the web dashboard or boot reports for observability?

## Next Steps

1. Review and ratify the combined roadmap with AI and platform leads.
2. If approved, draft ADR updates clarifying routing policies and conversation contracts.
3. Schedule Phase 1 implementation, ensuring adapters (Ollama, OpenAI) declare provisional metadata for validation.
