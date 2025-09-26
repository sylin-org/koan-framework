## **Migration & Adoption Guide**

### 1. Starting Point
- Current sample already runs via docker-compose with API, Angular client, MongoDB, Weaviate, and Ollama.
- API exposes legacy `/api/files/*` endpoints with minimal functionality; Angular calls expect richer contract.
- Goal: migrate to new domain models, pipeline, and APIs without breaking compose-based setup.

### 2. Incremental Migration Strategy

| Step | Description | Safeguards |
|------|-------------|------------|
| **1. Dual-write models** | Introduce new `SourceDocument` entity alongside existing `File` while controllers still serve legacy contract. | Wrap writes so both schemas stay in sync; add feature flag to opt into new pipeline. |
| **2. Background pipeline shadowing** | Run `DocumentAnalysisPipeline` in parallel but mark results as experimental; expose via new timeline endpoint for verification. | Keep legacy synchronous processing active until parity confirmed. |
| **3. Controller switchover** | Gradually replace `/api/files` routes with `/api/documents` equivalents; provide temporary proxy endpoints returning both old + new payloads. | Contract tests ensuring Angular + MCP clients handle new DTOs. |
| **4. UI update** | Flip Angular feature flag to use new services; remove legacy adapters. | Ship toggle for workshop demos to revert if needed. |
| **5. Clean-up** | Delete legacy entities/services once adoption complete. | Mongo migration script to drop old collections once confirmed empty. |

### 3. Data Migration Checklist
- [ ] Snapshot existing Mongo collections (`files`, `documenttypes`) before schema changes.
- [ ] Execute migration script creating `source_documents`, `semantic_type_profiles`, `document_chunks`, `document_insights`, `document_processing_events`.
- [ ] Backfill `Summary` field for existing documents using current analysis text (if available).
- [ ] Generate embeddings only when Weaviate reachable; otherwise leave null and schedule backfill job.
- [ ] Verify counts between old and new collections; document any records skipped with reasons.

### 4. Code Migration Cheat Sheet
- Replace `File` references with `SourceDocument` in server + client code; rename TypeScript interfaces accordingly.
- Swap `FileAnalysisService` for `DocumentAnalysisPipeline` orchestrated flow; controllers call dispatcher instead of service directly.
- Migrate `DocumentTypesController.Generate` to `TemplatesController.Generate` using new prompt structures.
- Update Angular API services to `DocumentsApiClient`, `TemplatesApiClient`, `InsightsApiClient`, `ModelsApiClient` generated from OpenAPI.
- Update `Program.cs` to call `AddDocMind()` and remove manual DI registrations.

### 5. Rollback Plan
- Keep legacy controllers/services behind feature flag `DocMind:Features:LegacyApi` until new stack stable.
- Provide script `scripts/docmind-rollback.sh` that restores Mongo backup, reverts configuration, and restarts compose stack.
- Maintain documentation for enabling/disabling new Angular feature toggles (`environment.ts`).

### 6. Communication & Documentation
- Update README quick-start to highlight new endpoints, timeline feature, and replay tooling.
- Provide migration FAQ detailing new terminology (SourceDocument vs File, SemanticTypeProfile vs Type).
- Document manual verification steps (upload sample, verify timeline, accept template suggestion, run MCP tool).

### 7. Acceptance Criteria
- ✅ New endpoints deliver same or better data compared to legacy ones; Angular works without console errors.
- ✅ Pipeline handles supported formats (PDF, DOCX, TXT, PNG) within documented SLAs.
- ✅ Timeline and insights visible in UI, Postman, and MCP clients.
- ✅ Compose stack remains single-command setup; optional overrides documented.
- ✅ Legacy code removed or feature-flagged with clear deprecation timeline.

This migration guide ensures teams can transition from the existing minimal implementation to the fully realized DocMind refactor while maintaining stability for demos and workshops.
