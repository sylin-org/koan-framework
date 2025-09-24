# S12.MedTrials sample proposal

## Executive summary

S12.MedTrials introduces a clinical trial operations copilot that fuses Koan's AI and MCP pillars. The sample ships a runnable API, AngularJS single-page app, and parity test suite that demonstrate how study coordinators, IDE copilots, and autonomous agents can share orchestration, diagnostics, and approval guardrails when scheduling visits and reporting safety issues.

## Objectives

1. Showcase AI-assisted compliance loops (protocol ingestion, safety summarisation, visit optimisation) that reuse Koan's `IAi.EmbedAsync` and `IAi.ChatAsync` abstractions.
2. Validate MCP parity by exposing entity CRUD plus a bespoke "Plan Visit Adjustments" tool that mirrors the REST pipeline via `EndpointToolExecutor` and the translators.
3. Provide an AngularJS + Bootstrap UX hosted under the API project's `wwwroot`, matching the style and technology choices proven in the SPA samples (S10/S11 lineage) so teams can reuse layout components, routing, and controllers without new dependencies.【F:samples/S10.DevPortal/wwwroot/index.html†L1-L74】【F:samples/S10.DevPortal/wwwroot/js/app.js†L1-L38】
4. Document cross-pillar guardrails (scope-checked mutations, diagnostics propagation, rate limit surfacing) so future AI+MCP samples have a canonical template to follow.

## Scenario overview

Clinical operations teams coordinate multi-site studies that must adhere to strict regulatory timelines while protecting participant safety. The service must:

- Track site readiness (`TrialSite`), participant schedules (`ParticipantVisit`), and safety incidents (`AdverseEventReport`).
- Accept protocol amendments and monitoring notes, then convert them into embeddings for semantic recall.
- Assemble contextual briefs that cite source documents when AI suggests visit adjustments or adverse event summaries.
- Require scoped approvals before modifications reach live schedules, ensuring parity between REST and MCP mutations.

## Domain design

### Entities

| Entity | Purpose | Key fields |
| --- | --- | --- |
| `TrialSite` | Operational state of each clinic/site | `Id`, `Location`, `PrincipalInvestigator`, `EnrollmentTarget`, `CurrentEnrollment`, `RegulatoryStatus`, `VectorId` |
| `ParticipantVisit` | Scheduled, completed, or proposed visits per participant | `Id`, `ParticipantId`, `TrialSiteId`, `VisitType`, `ScheduledAt`, `Status`, `ProposedAdjustments`, `Diagnostics` |
| `AdverseEventReport` | Safety incidents with severity, narrative, and follow-up actions | `Id`, `ParticipantId`, `TrialSiteId`, `Severity`, `Description`, `OnsetDate`, `Status`, `VectorId`, `SourceDocs` |
| `ProtocolDocument` | Amendments, lab manuals, or regulatory guidance captured for retrieval | `Id`, `TrialId`, `Version`, `DocumentType`, `FileRef`, `ExtractedText`, `VectorId`, `Tags`, `EffectiveDate` |
| `MonitoringNote` | CRA/QA findings tied to visits or sites | `Id`, `TrialSiteId`, `ParticipantVisitId`, `NoteType`, `Summary`, `FollowUpRequired`, `EnteredBy`, `VectorId` |

### Data boundaries

- `ProtocolDocument` and `AdverseEventReport` share vector lifecycle management so their embeddings can be re-indexed together when models change.
- `ParticipantVisit.ProposedAdjustments` uses value objects (e.g., `VisitAdjustment`) to keep entity persistence trivial for `EntityController` scaffolding.
- Vector embeddings align with `TrialSite` scope to support site-centric retrieval and guard regulated data residency boundaries.

## AI workflows

1. **Protocol ingestion**
   - REST endpoint `POST /protocol-documents/ingest` accepts files/URLs, extracts text via a background service, calls `IAi.EmbedAsync`, and stores vectors alongside metadata. Diagnostics and warnings (e.g., truncated scans) bubble through `ResponseTranslator` for MCP parity.
   - The ingestion worker follows the S5.Recs seeding pattern: resolve AI providers with `Ai.TryResolve`, short-circuit when vector storage is unavailable, and batch embeddings into `Vector<T>.Save` writes so the sample behaves even if Weaviate/Ollama are offline.【F:samples/S5.Recs/Services/SeedService.cs†L554-L645】
2. **Compliance retrieval & Q&A**
   - `POST /protocol-documents/query` accepts natural language questions, embeds the query, and returns nearest `ProtocolDocument` matches with citations for UI display.
   - The retrieval pipeline mirrors S5.Recs by composing embeddings through `AiEmbeddingsRequest`, checking `Vector<T>.IsAvailable`, and falling back to deterministic database queries and demo payloads when AI services fail so UX and MCP callers still receive responses.【F:samples/S5.Recs/Services/RecsService.cs†L75-L169】
3. **Visit schedule optimiser**
   - `POST /participant-visits/plan-adjustments` gathers enrollment projections, site capacity, and protocol windows. It composes a prompt with contextual citations, calls `IAi.ChatAsync`, parses structured JSON suggestions, and materialises draft `ParticipantVisit` adjustments flagged as `Proposed` pending approval.
   - Model selection respects the S5 configuration helper sequence (`Configuration.ReadFirst`) so local Ollama, discovered services, or hosted providers can be swapped without code changes; embeddings reuse the same helper when stitching protocol and monitoring context.【F:samples/S5.Recs/Services/RecsService.cs†L202-L283】
4. **Safety digest**
   - Nightly job summarises serious adverse events and overdue follow-ups, posts them to Koan Messaging for escalation, and exposes the digest via an MCP tool for agent monitors.
   - The job reuses the hybrid scoring guardrails from the recommendation sample (vector + popularity fallbacks) to weigh incidents when AI is offline, ensuring alerts still ship with deterministic heuristics.【F:samples/S5.Recs/Services/RecsService.cs†L320-L399】

### AI integration blueprint (grounded in S5.Recs)

- **Provider resolution and graceful degradation** – All AI entry points guard `Ai.TryResolve`, gate vector usage behind `Vector<T>.IsAvailable`, and cascade to deterministic heuristics or demo payloads exactly like the recommendation service so demos stay functional without Ollama/Weaviate.【F:samples/S5.Recs/Services/RecsService.cs†L75-L169】
- **Embedding text construction** – Protocol sections, site notes, and adverse event narratives are embedded using the batched helper pattern that concatenates titles, synopsis, and tags before calling `AiEmbeddingsRequest`, keeping cosine similarity quality high while respecting batch limits.【F:samples/S5.Recs/Services/SeedService.cs†L554-L687】
- **User/agent personalisation hooks** – Visit recommendations and safety digests leverage the hybrid scoring blend (vector score + popularity/policy weights) showcased in S5 so agents receive ranked outputs with explainable “reasons” payloads.【F:samples/S5.Recs/Services/RecsService.cs†L320-L399】

### Configuration blueprint

- Base `appsettings.json` ships the Ollama defaults/required models just like S5.Recs so contributors can run locally with the lightweight `all-minilm` embedding model.【F:samples/S5.Recs/appsettings.json†L1-L21】
- Development settings connect to MongoDB + Weaviate, register the JWT test provider, and grant AI scopes mirroring the recommendation sample so SPA and MCP traffic can authenticate without extra infrastructure.【F:samples/S5.Recs/appsettings.Development.json†L12-L74】
- Docker Compose profile exposes the same environment variables for Ollama discovery, vector endpoints, and JWT issuance so one command launches API, Mongo, Weaviate, and Ollama together for integration testing.【F:samples/S5.Recs/docker/compose.yml†L1-L64】

## MCP integration

- Decorate entities with `McpEntityAttribute`, enabling read tools (`trial-site.collection`, `protocol-document.collection`) by default and gating mutations (`participant-visit.upsert`, `adverse-event-report.update`) behind scopes such as `clinical:operations` and `clinical:safety`.
- Implement a `participant-visit.plan` MCP tool using `EndpointToolExecutor` to call the REST planning pipeline, ensuring shared validations and diagnostic payloads reach STDIO callers.
- Extend parity tests so REST and MCP surfaces agree on:
  - Validation failures (e.g., protocol window violations, missing approvals)
  - Rate-limit headers from AI providers
  - Diagnostics returned when AI yields low confidence suggestions

## Web client plan (AngularJS SPA)

### Baseline stack alignment

- Reuse the AngularJS 1.8 + `ngRoute` bootstrapper pattern where `index.html` hosts the navbar, `ng-view`, and vendor CDNs, matching the existing sample layout served from `wwwroot`.【F:samples/S10.DevPortal/wwwroot/index.html†L1-L54】
- Follow the modular structure (`wwwroot/js/app.js`, `api.js`, `controllers.js`, `views/`) so routers and controllers mirror the S11-style sample and can plug into Koan's REST endpoints without new build tooling.【F:samples/S10.DevPortal/wwwroot/js/app.js†L1-L38】【F:samples/S10.DevPortal/wwwroot/js/controllers.js†L1-L75】
- Keep Bootstrap 5 and Font Awesome for responsive layout and iconography, per the established pattern.【F:samples/S10.DevPortal/wwwroot/index.html†L8-L32】

### S12 UX blueprint

Routes (`ngRoute`) map to:

1. **Trial Overview (`#/overview`)**
   - Hero panel summarising enrollment vs. target, protocol version status, and outstanding monitoring findings.
   - Tables/cards for sites with quick filters (region, regulatory status) and embedded citations so coordinators can open supporting documents.
   - Compliance ticker surfacing warnings emitted by `ResponseTranslator` (e.g., AI low confidence, rate limits).
2. **Visit Planning (`#/visits`)**
   - Form to select cohorts, adjust visit windows, toggle AI planner parameters (temperature, provider), and trigger `participant-visit.plan` endpoint.
   - Side-by-side view comparing AI-generated adjustments vs. current schedule; approvals tracked via callouts and scope badges mirroring S10 capability cards.【F:samples/S10.DevPortal/wwwroot/js/controllers.js†L12-L61】
   - Inline chat log showing AI reasoning excerpts with citations; allow manual edits before committing.
3. **Safety Hub (`#/safety`)**
   - Semantic search input tied to `protocol-documents/query` and `adverse-event-reports/query`, returning matched events with severity badges and source links.
   - Timeline view for recent adverse events, highlighting items escalated by the nightly digest.
4. **Document Library (`#/documents`)**
   - Filterable list of protocol amendments, lab manuals, and monitoring notes with download actions.
   - Vector-powered quick search to jump to relevant clauses; show when data was last re-embedded.

Shared components:

- Global alert/toast system wired to `$rootScope.showAlert`, reusing the floating alert implementation already present in the Angular baseline.【F:samples/S10.DevPortal/wwwroot/js/app.js†L22-L38】
- Provider indicator toggling between AI providers (OpenAI/Azure/local) so operators see which inference backend responded, mirroring the status badge pattern.【F:samples/S10.DevPortal/wwwroot/index.html†L33-L44】
- Loading spinners and capability badges styled via `style.css`, extended with clinical branding.

## Implementation plan

1. **Foundation (Week 1)**
   - Scaffold API project from Koan template; define entities and controllers via `EntityController<T>`.
   - Configure vector index and AI provider settings.
   - Copy Angular baseline structure into `wwwroot`, stub out routes/views with placeholder data.
2. **AI & MCP wiring (Week 2)**
   - Implement ingestion, retrieval, planner, and digest endpoints; wire MCP entity exposure and custom planner tool.
   - Stand up parity tests verifying translator behaviour and AI diagnostics propagation.
3. **UX refinement (Week 3)**
   - Flesh out Angular controllers/services to call REST endpoints, surface AI responses with citations, and manage approvals.
   - Style dashboards and components to reflect MedTrials branding while staying within Bootstrap/AngularJS idioms.
4. **Hardening (Week 4)**
   - Load-test planner loops with fixture data, tune concurrency limits, and validate MCP STDIO heartbeat integration.
   - Finalise documentation (decision, proposal, usage guide) and publish walkthrough videos/screenshots.

## Quality gates

- REST vs MCP parity tests for planner success/failure paths.
- Unit tests for AI service wrappers (embedding, chat orchestrations).
- Cypress or Playwright smoke for SPA navigation + planner interaction (optional stretch goal).
- Manual validation of scoped mutations to ensure unauthorized MCP callers receive consistent errors.

## Risks & mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| AI planner hallucinations | Incorrect visit adjustments | Constrain prompts to structured JSON, enforce schema validation before persisting drafts, surface confidence metrics in UI. |
| Vector index drift | Outdated embeddings degrade retrieval | Schedule re-embedding of stale documents, track embedding version field on entities. |
| MCP tool misuse | Unauthorized schedule changes | Gate mutations behind `clinical:operations`/`clinical:safety` scopes, require manual approval before status transitions to `Approved`. |
| SPA tech debt | AngularJS longevity concerns | Align with existing sample stack to avoid new dependencies, document migration considerations in README. |

## Open questions

1. Should we expose additional AI providers (e.g., local vLLM) in sample configs to demonstrate failover?
2. Do we need WebSocket streaming for planner responses, or is long-polling adequate for the sample timeline?
3. Which regulatory reports (e.g., SUSAR exports) should the sample generate for its initial release?
