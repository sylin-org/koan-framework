# ARCH-0061: JSON Layer Unification on Newtonsoft (JToken) for Dynamic Execution & MCP

Status: Accepted  
Date: 2025-10-08  
Deciders: Koan Core Team  
Consulted: Data, Web, MCP subsystem maintainers  

## Context

Koan historically mixed `System.Text.Json` (STJ) types (`JsonNode`, `JsonDocument`) with Newtonsoft types (`JObject`, `JToken`) in different layers:

- Web & entity controllers primarily serialized via STJ (ASP.NET defaults).
- Dynamic / scripting / AI-oriented surfaces (earlier prototypes) occasionally relied on Newtonsoft for richer DOM manipulation.
- Introduction of "Code Mode" (S16) added a Jint-based JavaScript execution environment requiring:
  - Late-bound inspection & mutation of arbitrary JSON payloads.
  - Uniform cloning, deep traversal, and selective projection.
  - Predictable surface for tool invocation envelopes & SSE streaming events.

The mixed approach caused friction:
- Frequent translation (STJ <-> Newtonsoft) overhead & accidental shape drift.
- Type signature churn across MCP transport boundaries (e.g., `JsonNode` vs `JToken`).
- Complication in test harnesses (different parsing libraries creating subtle comparison mismatches).
- Increased risk of partial migrations introducing compile errors during refactors.

## Decision

Unify all Model Context Protocol (MCP) and dynamic execution pathways (code-mode executor, RPC translators, SSE transport, tool execution results, entity dynamic façade) on a single JSON abstraction backed by **Newtonsoft `JToken`**. Replace direct `System.Text.Json` DOM usage inside these components. Externally (e.g., standard MVC pipeline) we allow STJ to remain for now—no hard fork of ASP.NET serialization settings yet.

A thin `IJsonFacade` abstraction anchors future portability; current implementation delegates to Newtonsoft. All dynamic entity and execution plumbing (EntityDomain proxies, RequestTranslator, ResponseTranslator, McpRpcHandler, HttpSseTransport, ServerSentEvent) now accept/return `JToken` instances.

## Rationale

1. Dynamic Operations: `JToken` API breadth (mutation, deep clone, path selection) materially reduces glue code vs STJ's minimal DOM.
2. Scripting Symmetry: Code-mode (Jint) bridging is simpler when marshaling a single JSON object model repeatedly rather than dual representations.
3. Transport Consistency: JSON-RPC & SSE envelopes become stable and testable with deterministic formatting.
4. Reduced Cognitive Load: Contributors no longer decide ad hoc between two DOMs in MCP-related code.
5. Future Extensibility: Facade enables pivot (e.g., to STJ once feature parity is acceptable or to a high-performance pooled DOM) without cascading signature changes.

## Scope

Applies to:
- MCP runtime: RPC handler, request/response translation, tool execution pipeline.
- Code-mode execution: Jint code executor & SDK entity proxies.
- Streaming & transport: SSE event modeling and HTTP bridge.
- Test suites targeting code-mode parity.

Out of scope (Phase 1):
- Global ASP.NET JSON options (controllers remain on STJ serializers for now).
- Legacy or deprecated samples.
- Broad test utilities still referencing STJ (inventoried separately; see TEST impacts).

## Alternatives Considered

| Option | Reason Rejected |
| ------ | --------------- |
| Stay Mixed (STJ + Newtonsoft) | Continues translation churn; harder to evolve scripting model. |
| Full Migration to STJ DOM | Lacks mature mutation & path ergonomics; higher code volume for dynamic features. |
| Custom Unified Abstraction Layer First | Added abstraction cost before consolidation; delaying problem resolution. |
| Hybrid Adapter (convert at boundary only) | Still exposes dual mental model; risk of silent divergence. |

## Consequences

Positive:
- Simpler dynamic feature evolution (payload transformers, runtime tool shaping).
- Lower test flake rates due to deterministic JSON representation.
- Clear boundary for future performance profiling (single JSON provider).

Negative / Trade-offs:
- Newtonsoft dependency weight retained in runtime path.
- Slight runtime allocation overhead vs STJ DOM for simple payloads.
- Dual serializer reality temporarily persists (MVC vs MCP) until Phase 2 alignment.

Mitigations:
- Centralize creation/cloning via facade to ease future optimization.
- Avoid leaking Newtonsoft specifics outside MCP/dynamic layers.
- Phase 2 ADR (future): unify controller serialization or introduce negotiated formatting.

## Implementation Summary

Completed migration steps:
- Replaced `JsonNode` / `JsonDocument` usage with `JToken` across: `EntityDomain`, `EndpointToolExecutor`, `RequestTranslator`, `ResponseTranslator`, `McpRpcHandler`, `HttpSseTransport`, `ServerSentEvent`, and `McpToolExecutionResult`.
- Updated code-mode parity tests to parse & assert via `JToken` exclusively.
- Added guards & improved error messages for enum and Guid coercion during argument translation.
- Introduced consistent short-circuit / diagnostics payload modeling with `JObject`.

Removed technical debt:
- Eliminated hybrid array/collection wrapping discrepancies caused by cross-DOM conversions.
- Removed brittle JS-side list membership boolean pattern (moved assertion server-side).

## Risks & Open Questions

| Risk | Impact | Mitigation |
| ---- | ------ | ---------- |
| Performance regression vs STJ for large serialization sets | Moderate | Future perf profiling; consider pooled writers or hybrid serializer (facade swap). |
| Contributor drift back to STJ DOM in MCP code | Inconsistency | Add lint/checklist in contribution guidelines; reference ADR in PR templates. |
| Phase 2 (controllers) never executed leading to permanent dual-stack | Ongoing complexity | Track follow-up epic; add KPI (remove Newtonsoft from non-dynamic paths or unify both). |
| Test layers still using STJ utilities cause subtle differences | Assertion fragility | Scheduled incremental migration guided by inventory. |

## Migration Plan (Executed)
1. Identify MCP surface JSON touchpoints (executor, translation, transport, entity proxies).
2. Introduce `IJsonFacade` abstraction and Newtonsoft implementation.
3. Atomically replace types & signatures to avoid transient hybrid compile errors.
4. Adapt tests and remove STJ parsing logic; assert via `JToken`.
5. Add negative-path tests to validate error and missing-entity handling under unified model.

## Follow-Up Tasks
- Author linting/script to flag new `System.Text.Json` DOM usage in MCP directories (Prevent regression).
- Evaluate consolidating ASP.NET controller serialization for future Phase 2 (consider facade integration or custom formatters).
- Performance benchmark: baseline JToken vs STJ for representative MCP payloads.
- Consider adding structured diff helper for `JToken` to improve diagnostics.

## References
- AI-0014-mcp-code-mode
- DATA-0073-jobject-for-dynamic-entities
- TEST-0002-test-parity-migration-roadmap
- ARCH-0052-core-ids-and-json-merge-policy

## Decision Outcome
Unified JSON handling has stabilized dynamic execution tests, reduced code duplication, and clarified extension points. The trade-offs (Newtonsoft dependency & dual serializer state) are acceptable short-term. Future phases will determine whether to converge globally on a single serializer or maintain a scoped dual strategy with stronger guardrails.
