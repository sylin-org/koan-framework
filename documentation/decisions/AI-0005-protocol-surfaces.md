# AI-0005 — Protocol surfaces (gRPC, OpenAI shim, MCP, AI-RPC)

Status: Proposed
Date: 2025-08-19
Owners: Koan Web

## Context

Teams integrate via different AI protocol surfaces. We need an approach that preserves Koan’s controller-first model while offering optional interoperability layers.

## Decision

- Native REST + SSE remains primary and authoritative with OpenAPI.
- gRPC is provided for internal S2S scenarios (opt-in, feature flagged).
- OpenAI-compatible shim: a scoped subset for practical interop, with a stability window and mapping table.
- Desirable adapters: MCP (Model Context Protocol) server role and AI-RPC mapping for chat/embeddings as optional packages.

## Consequences

- Protocol adapters must map headers/capabilities to Koan equivalents; differences documented.
- Testing includes compatibility suites; performance budgets compared to native REST.
