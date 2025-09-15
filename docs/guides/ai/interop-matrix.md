# AI Interop Matrix

Purpose
- Provide a concise mapping from Koan’s native REST + SSE endpoints to other AI protocol surfaces for teams that need interop.
- REST + SSE is authoritative. Other surfaces are optional adapters; scope and limits are documented here.

Native endpoints (authoritative)
- POST /ai/chat (SSE streaming supported)
- POST /ai/embed
- POST /ai/rag/query (SSE streaming supported)
- GET /ai/models

Common headers (native)
- Koan-AI-Provider, Koan-AI-Model, Koan-AI-Streaming: true|false
- Koan-Session-Id, Koan-Tenant, Koan-Project
- Koan-Vector-Provider (for RAG)

gRPC mapping (internal S2S; optional)
- Service AiChatService
  - rpc Chat(stream ChatRequest) returns (stream ChatChunk)
- Service AiEmbeddingsService
  - rpc Embed(EmbeddingRequest) returns (EmbeddingResponse)
- Service AiRagService
  - rpc Query(stream RagRequest) returns (stream RagChunk)
Notes
- Bidirectional streaming mirrors SSE. Metadata carries tenant/session/model.
- Feature flagged; versioned proto package: Koan.ai.v1

OpenAI-compatible shim (scoped subset; optional)
- POST /v1/chat/completions → maps to /ai/chat
- POST /v1/embeddings → maps to /ai/embed
- GET /v1/models → maps to /ai/models
Notes
- Subset only (documented in ADR AI-0005). Provider/model overrides via native headers or body hints. RAG has no direct analog in shim.

MCP (Model Context Protocol) adapter (desirable; optional)
- Tools
  - Koan.chat tool → calls /ai/chat
  - Koan.embed tool → calls /ai/embed
  - Koan.rag tool → calls /ai/rag/query
- Resources
  - Koan.models resource → enumerates /ai/models
Notes
- Tool allow-list integrates with Koan’s safe tool registry; tenant/session propagated in resource params.

AI-RPC adapter (desirable; optional)
- Methods
  - ai.chat → /ai/chat
  - ai.embed → /ai/embed
Notes
- Map method metadata to Koan headers; streaming parity depends on AI-RPC client capabilities.

Headers/metadata mapping
- Tenant/Project: gRPC metadata keys (Koan-tenant, Koan-project); OpenAI shim uses API key scoping if available; MCP/AI-RPC pass via params/metadata.
- Provider/Model: prefer Koan-AI-Provider/Koan-AI-Model headers; for shim, use model field mapping.
- Streaming: gRPC streams by default; REST uses SSE; shim uses stream:true when supported by client.

Capabilities and limits
- Tool-calling: available natively; shim exposure limited; MCP integrates via tool registry.
- Moderation/budgets: enforced natively regardless of adapter.
- RAG: native only (no standard shim); MCP tool exposes it explicitly.

Testing
- Compatibility suites per adapter; perf budget comparisons vs. native REST; SSE → stream parity cases.

References
- ADR: AI-0005 — Protocol surfaces (gRPC, OpenAI shim, MCP, AI-RPC)
- Epic: Native AI Koan — W2 story
