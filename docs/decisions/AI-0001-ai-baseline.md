# AI-0001 — Native AI baseline (scope, contracts, discovery, streaming)

Status: Accepted
Date: 2025-08-19
Owners: Koan Core

## Context

Koan needs native AI capabilities that match its principles: controllers-only HTTP, capability discovery, sane defaults, and strong observability. Teams want turnkey inference/RAG without lock-in or excessive configuration.

## Decision

- Scope: focus on inference and RAG (chat, embeddings, vector search, transformers). Training/experiment tracking are out of scope; integrate via guides.
- Contracts: define IChatCompletion, IEmbedding, IVectorStore, IToolCall, AiOptions, AiCapabilityFlags.
- Discovery: surface provider/vector capabilities (streaming, tool-use, max tokens, dims/metrics) via options and boot report.
- Streaming: SSE is the default for /ai/chat; gRPC is optional for internal S2S; OpenAI-compatible shim is scoped.
- Observability: OTel spans/metrics for tokens, cost, latency; prompt hashing; redaction by default.
- Governance: budgets (token/time), model allow-lists, safe tool registry.

## Consequences

- Package structure: Koan.AI.Core, Koan.AI.Providers.*, Koan.Data.Vector.*, Koan.Web.AI.
- Clear lines with existing Data/Web/Messaging; shared constants and headers.
- Training/registry remain integrations, not core.
