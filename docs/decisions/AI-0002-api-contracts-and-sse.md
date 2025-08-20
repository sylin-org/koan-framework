# AI-0002 — AI API contracts and SSE format

Status: Proposed
Date: 2025-08-19
Owners: Sora Web

## Context

Surface stability reduces churn. We need request/response DTOs, headers, and streaming semantics pinned before broad implementation.

## Decision

- Endpoints: /ai/chat, /ai/embed, /ai/rag/query; ProblemDetails for errors.
- Headers: Sora-AI-Provider, Sora-AI-Model, Sora-AI-Streaming, Sora-Session-Id, Sora-Tenant, Sora-Project.
- SSE: event names (token, tool, end), heartbeat frames, explicit termination; JSON lines for event data.

## Consequences

- OpenAPI must include examples; controllers conform; tests verify SSE framing and termination.
