# S13.DocMind Next Steps (Updated After Delta Review)

## Iteration 0 – Bedrock Contracts & Bootstrap Recovery
1. **Repair storage + entity contracts**
   - Introduce `DocumentStorageLocation` on `SourceDocument`, refactor intake/storage services to persist the new value object, and update extraction services to resolve files via `IDocumentStorage` instead of the removed `Storage` property.
   - Remove inline embeddings from `SemanticTypeProfile`, add persistence helpers for `SemanticTypeEmbedding`, and migrate existing code to the adapter-based API.
2. **Harden configuration + registrar wiring**
   - Bind `DocMindOptions` from the `DocMind` configuration section with validation, align default values with the proposal (queue retries, chunk size, AI models), and document the environment variables in appsettings/compose files.
   - Update `Program.cs` to load Koan modules and ensure `DocMindRegistrar` registers hosted workers, health checks, and boot diagnostics per the infrastructure blueprint.
3. **Stabilize diagnostics contracts**
   - Ensure `DocumentProcessingEvent` entries receive populated `Detail`, `Context`, and `Metrics` fields that match controller DTOs; add repository helpers for efficient timeline queries.

## Iteration 1 – Data Services & Processing Orchestration
1. **Normalize storage + repository layer**
   - Return a structured descriptor (hash, provider, physical path) from `LocalDocumentStorage`, and add repositories for `SourceDocument` and `DocumentProcessingEvent` to eliminate `Entity<T>.All()` scans.
   - Extend the vector bootstrapper to ensure both chunk and semantic embedding indices exist, seeding baseline semantic profiles if missing.
2. **Rebuild the background worker**
   - Replace the channel-heavy queue with the proposal’s polling `DocumentProcessingWorker` (or wrap the queue behind that abstraction), honoring concurrency and retry settings from `DocMindOptions`.
   - Implement stage progression (text extraction → vision → insights → embeddings → template suggestion) with atomic `SourceDocument.Status/Summary` updates and durable event logging.
3. **Retry + telemetry improvements**
   - Respect the requested stage when requeuing work, mark documents failed when retries are exhausted, and emit context-rich events (chunk/page counts, token usage, contains-images flag) to back the diagnostics UI.

## Iteration 2 – AI, Insights, and Discovery Enablement
1. **OCR & vision integration**
   - Wire `VisionInsightService` to `IAi.VisionPrompt`, capture OCR/vision results with confidence metrics, and persist related `DocumentInsight` records plus summary flags.
2. **Structured insight synthesis**
   - Update `InsightSynthesisService` to compile against the new chunk model, replace placeholder summaries with prompt-driven structured payloads, and generate embeddings through adapter helpers.
3. **Discovery layer rebuild**
   - Refactor aggregation/suggestion services and controllers to surface structured insights, collection summaries, and vector-powered template recommendations consistent with the proposal.

## Iteration 3 – Experience, MCP, and Operational Hardening
1. **API & MCP alignment**
   - Regenerate MCP tools/contracts and update controllers to expose the enriched timeline, chunk, and insight endpoints, including retry/replay workflows.
2. **Testing + observability**
   - Add integration tests (intake dedupe, pipeline progression, retry exhaustion) using Koan in-memory adapters, and emit OpenTelemetry spans/metrics around AI calls and storage operations.
3. **Documentation & tooling refresh**
   - Revise README/app guidance to reflect the new bootstrap, environment setup, and AI/vector prerequisites; provide reset scripts and sample fixtures to accelerate adoption.
