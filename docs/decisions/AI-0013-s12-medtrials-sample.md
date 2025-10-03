# AI-0013 - S12.MedTrials sample adoption

Status: Accepted
Date: 2025-02-15
Owners: Koan Samples Guild

## Context

Koan's existing samples highlight CRUD, messaging, and vector primitives, yet none demonstrate how the AI and MCP pillars work together inside a regulated clinical operations workload. Partners asked for a flagship scenario that exercises audit trails, approval hooks, and scoped MCP tooling while remaining runnable with Koan's thin defaults.

## Decision

Adopt **S12.MedTrials** as the flagship AI + MCP reference:

- Model a clinical trial operations domain with `TrialSite`, `ParticipantVisit`, and `AdverseEventReport` entities so coordinators can monitor enrollment, safety, and compliance from a single surface.
- Provide REST endpoints that embed protocol changes, semantically search guidance, and summarise adverse events via `IAi.EmbedAsync` and `IAi.ChatAsync`, ensuring diagnostics, rate-limit headers, and warnings flow through `ResponseTranslator` for MCP parity.
- Reuse the S5.Recs guardrails for provider resolution, vector availability checks, and batched embeddings so MedTrials runs locally without Ollama or Weaviate while still lighting up vector search when configured.【F:samples/S5.Recs/Services/RecsService.cs†L75-L169】【F:samples/S5.Recs/Services/SeedService.cs†L554-L645】
- Expose matching MCP tools with `McpEntityAttribute` and add a compliance-focused `EndpointToolExecutor` wrapper that lets agents request risk digests while honouring the same approval gates and diagnostics as REST clients.
- Ship an AngularJS + Bootstrap SPA under the API project's `wwwroot` (matching S11) so contributors can validate AI experiences across browser, REST, and MCP surfaces.

## Consequences

- Koan gains a clinical operations sample that demonstrates AI-assisted summarisation, scope-aware mutations, and MCP parity in a regulated domain, giving contributors a realistic template for healthcare partners.
- Documentation, UX guidance, and parity tests align on one flagship example, reducing ambiguity when onboarding AI + MCP workloads.
- Flow, Messaging, and Data teams receive concrete requirements for audit logging, hook enforcement, and vector pipelines triggered by AI-driven scheduling and compliance workflows.

## Alternatives considered

- **Retain the S12.ResilienceOps concept:** Rejected because clinical trial sponsors specifically requested a compliance-first reference implementation.
- **Iterate on S8.Location:** Rejected; the location sample lacks the approval checkpoints, adverse event reporting, and audit diagnostics needed for regulated healthcare scenarios.

## Status & Migration

- Status: Accepted for immediate implementation. Sample scaffolding, documentation, SPA, and parity tests ship together.
- Migration: Future AI/MCP samples should draw from the patterns codified in S12.MedTrials; retire earlier S8 guidance once partner feedback validates the new bundle.
