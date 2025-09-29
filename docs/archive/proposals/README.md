# Proposal archive

**Contract**

- **Audience**: Framework maintainers and historians looking for prior research.
- **Inputs**: Legacy proposals formerly scattered under `/documentation/proposals`.
- **Outputs**: Current disposition, successor ADR (when available), and follow-up notes.
- **Error modes**: Treat everything here as non-authoritative; verify against active ADRs before acting.
- **Success criteria**: Readers can tell at a glance whether a proposal is superseded, still pending, or intentionally parked.

**Edge cases**

1. Some proposals reference namespaces that have since been renamed; validate before copying code.
2. Dates pre-2024 often assume Zen-era bootstrap; expect drift in configuration samples.
3. Any item without an explicit successor ADR needs a maintainer to capture the decision formally.
4. Vector + AI proposals predate `Koan.AI`; prefer the adapters described in `AI-0005` onward.
5. Relationship refactors here were partially implemented—see `DATA-0072` and `FLOW-0102` for the canonical state.

## Disposition summary

| Proposal | Status | Notes |
| --- | --- | --- |
| [Parent relationship system](parent-relationship-system.md) | **Superseded** | Incorporated into `DATA-0072` and `FLOW-0102`; keep for historical context only. |
| [Implementation roadmap](implementation-roadmap.md) | **Superseded** | Schedule replaced by the phased delivery in `DX-0039`; use as a planning template. |
| [Relationship response format v2](relationship-response-format-v2.md) | **Archived** | Concepts folded into the Koan entity transformers described in `WEB-0035`. |
| [Koan MCP HTTP/SSE transport](koan-mcp-http-sse-transport.md) | **Partially adopted** | Transport primitives now governed by `AI-0012` and `AI-0013`; revisit gaps during MCP hardening. |
| [Adapter infrastructure centralization](PROPOSAL_Adapter_Infrastructure_Centralization.md) | **Needs ADR** | Core ideas live in the new orchestration stack; capture remaining deltas in a follow-up OPS ADR. |
| [Provider readiness system](PROPOSAL_Provider_Readiness_System.md) | **Superseded** | Mechanism replaced by the readiness matrix in `ARCH-0044`. |
| [Service adapter realignment](service-adapter-realignment.md) | **Superseded** | Decisions folded into `ARCH-0045` and `ARCH-0047`; keep only for citation. |
| [Koan Aspire analysis set](koan-aspire-architecture-review.md) | **Archived** | Outcomes ratified in `ARCH-0055`; retain for historical appendices. |
| [Koan Aspire implementation roadmap](koan-aspire-implementation-roadmap.md) | **Archived** | Implementation now tracked via ADR `ARCH-0055`. |
| [Koan Aspire technical specification](koan-aspire-technical-specification.md) | **Archived** | Specifics superseded by the approved integration doc `ARCH-0055`. |
| [Koan MCP integration](koan-mcp-integration.md) | **Archived** | Superseded by the MCP ADR set (`AI-0012` / `AI-0013`). |
| [Pagination attribute system](pagination-attribute-system.md) | **Superseded** | Pagination semantics now defined in `DATA-0061`; attribute concept dropped. |
| [Entity endpoint service extraction](entity-endpoint-service-extraction.md) | **Pending triage** | Consider folding into a DX ADR once controller surface stabilises. |
| [Backup/restore comprehensive specification](backup-restore-comprehensive-specification.md) | **Pending triage** | Needs alignment with `Koan.Data.Backup` deliverables; schedule follow-up ADR. |
| [Entity ID storage optimisation](entity-id-storage-optimization.md) | **Archived** | ID policy governed by `ARCH-0052`; revisit only if new providers demand changes. |
| [Entity ID optimisation (appendix)](entity-id-optimization/) | **Reference only** | Contains exploratory notebooks and diagrams; no direct implementation plan. |
| [S10 DevPortal comprehensive proposal](s10-devportal-comprehensive-proposal.md) | **Archived** | Superseded by `/samples/S10.DevPortal` implementation guide. |
| [S12 MedTrials proposal](s12-medtrials-sample-proposal.md) | **Archived** | Implementation captured in ADR `AI-0013` and the shipped sample. |
| [Service authentication proposal](PROP-0052-service-authentication.md) | **Superseded** | Decisions merged into `DEC-0053`. |
| [Observability over escape hatches](PROP-0053-observability-over-escape-hatches.md) | **Pending triage** | Needs conversion into an OPS ADR with current telemetry posture. |
| [Koan data relationship refactoring](Koan-data-relationship-refactoring-proposal.md) | **Superseded** | See `DATA-0072` and `FLOW-0102`. |

## How to use this archive

1. **Start with ADRs** – Treat the ADR catalog as the source of truth. Use proposals only for historical context.
2. **Promote or delete** – When you implement an idea from this folder, either promote it into a new ADR or remove the file after documenting the outcome here.
3. **Annotate follow-ups** – Update the table whenever a proposal changes status, linking to the ADR or issue that captured the decision.
4. **Chunk for agents** – Long-form proposals should be split before handing to agents. Place generated chunks under `/docs/archive/chunks/`.

## Next actions

- Draft an OPS ADR distilling remaining orchestration notes from `PROPOSAL_Adapter_Infrastructure_Centralization.md`.
- Confirm whether `entity-endpoint-service-extraction.md` still reflects planned controller refactors; either promote into a DX ADR or retire it.
- Align the backup/restore specification with the current `Koan.Data.Backup` implementation and document gaps in a new ADR.