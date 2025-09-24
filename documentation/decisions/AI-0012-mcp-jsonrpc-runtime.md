# AI-0012 - MCP JSON-RPC Runtime Standard

Status: Accepted
Date: 2025-09-23
Owners: Koan Web, AI Platform Guild

## Context

- `Koan.Mcp` needs a stable JSON-RPC implementation spanning STDIO, HTTP+SSE, and WebSocket transports without duplicating protocol plumbing.
- Entity Endpoint Service extraction (Koan v0.2.18) gives MCP a protocol-neutral execution surface; we now require a matching transport layer that preserves parity with REST behaviour and Koan logging/telemetry standards.
- Prior experiments mixed bespoke JSON-RPC handling per transport, creating drift and fragile agent experiences.

## Decision

- Standardize on [`StreamJsonRpc`](https://github.com/microsoft/vs-streamjsonrpc) for all MCP transports. The library provides tested framing, cancellation, and duplex messaging support that matches MCP requirements.
- Introduce an `IMcpTransportDispatcher` abstraction that wraps StreamJsonRpc. Transports resolve the dispatcher and expose only Koan-flavoured APIs (session start, tool invocation, diagnostics) so a future runtime swap is localized.
- Serialize payloads with Koan''s System.Text.Json configuration (camelCase, relaxed escaping, shared converters) to keep behaviour consistent across surfaces.
- Emit structured events under the `Koan.Transport.Mcp` context and forward heartbeat/session metrics through the existing health subsystem. STDIO mode redirects console logging to STDERR or alternative sinks to protect the JSON-RPC stream.

## Implementation Notes

- `McpServer` composes transports, the dispatcher, and `IEntityEndpointService` orchestration. Each transport owns connection lifecycle but defers JSON-RPC message handling to the dispatcher.
- Schema translation results are cached per entity using `AggregateBags`, keyed by operation, to avoid repeated reflection when serving JSON-RPC requests.
- Contract tests exercise parity between REST controllers and MCP tools (success paths, validation failures, hook short circuits, cancellation) to guarantee behavioural alignment.
- Configuration flags gate STDIO/HTTP/WebSocket exposure; defaults keep remote transports disabled until explicitly opted in.

## Guidance for AI-Assisted Development

- Prefer working against `IMcpTransportDispatcher` rather than `StreamJsonRpc` directly. The dispatcher exposes Koan-friendly primitives and hides library specifics.
- When scaffolding transport features, ensure diagnostics log with the `Koan.Transport.Mcp` context and reuse logging helpers from `Koan.Core`.
- Tests for new MCP behaviours should compare results with REST expectations (headers, warnings, dataset routing) and live under the MCP parity suite.
- When authoring schema pipelines, reuse cached descriptors/`AggregateBags`; avoid per-request reflection or bespoke JSON payloads.

## Consequences

- New dependency on `StreamJsonRpc` for the `Koan.Mcp` package; include it in license/legal scanning and SBOM outputs.
- MCP transports gain uniform logging, diagnostics, and cancellation semantics, reducing support overhead and improving developer experience.
- Future runtime swaps require only dispatcher implementation changes plus targeted regression tests.
- Build and CI pipelines must run the MCP parity suite alongside REST smoke tests to catch divergences early.
