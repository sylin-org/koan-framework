---
id: SEC-0018
slug: mcp-self-description-is-gated-like-web
domain: Security
status: Accepted
date: 2026-10-21
title: MCP self-description is gated like Web and projected per caller
related:
  - SEC-0011
  - SEC-0017
  - WEB-0072
  - ARCH-0128
---

# SEC-0018: MCP self-description is gated like Web and projected per caller

## Context

Web serves `/.well-known/Koan/facts` only in Development or when `Koan:Web:ExposeObservabilitySnapshot` is set, else
`404`. The MCP `koan://facts` resource served the same facts to any MCP caller. The HTTP capability document
(`{base}/capabilities`) listed every registered entity tool, and the code-mode SDK definitions (`{base}/sdk/definitions`)
declared every registered entity, whatever the caller could call; the edge filter in front of them applies only when
`RequireAuthentication` is set, which defaults to the "looks deployed" heuristic (ledger NK-192).

## Decision

**Self-description discloses what Web discloses and lists what its caller can call.**

1. `ObservabilityExposure.Allowed` (Koan.Web) is the one exposure decision for runtime facts; `WellKnownController` and
   `RuntimeFactsResourceProvider` both ask it. `koan://facts` is listed and readable only when it allows, or for a
   `null` principal (privileged in-process tooling).
2. `IMcpCapabilityReporter.GetCapabilitiesFor(caller)` is the document the capabilities endpoint serves: transports as
   before, and the entity tools the caller may call under the active exposure mode, through the SEC-0011 decision. The
   unfiltered `GetCapabilities` stays for operators. A reporter that does not implement it lists no tools.
3. The SDK definitions endpoint declares only entities with at least one verb the caller may call.
4. Both endpoints and the Explorer map evaluate the request's own principal stamped with its connection's origin tier
   (SEC-0015, SEC-0016).

## Compatibility

- Outside Development, without `ExposeObservabilitySnapshot`, `koan://facts` disappears from `resources/list` and reads
  as empty.
- The capability document and SDK definitions shrink to what the caller can use; an anonymous caller no longer learns
  the names of walled entities and tools.
- `RuntimeFactsResourceProvider` and `McpCapabilityReporter` gain constructors that DI selects; the former
  `RuntimeFactsResourceProvider(IKoanRuntimeFacts)` constructor is obsolete and serves the facts to `null`-principal
  tooling only. `IMcpCapabilityReporter` gains a default-implemented member. No signature is removed.

## Evidence

- `RuntimeFactsGatingSpec` (Koan.Mcp.Conformance.Tests): on a Staging host `koan://facts` is neither listed nor
  readable; `RuntimeFactsResourceSpec` still reads it in Development. The Staging cell failed before this change.
- `SelfDescriptionGatingSpec` (Koan.Mcp.Streamable.IntegrationTests): an anonymous caller's capability document and SDK
  definitions omit an entity gated `authenticated` on every verb. Both failed before.
