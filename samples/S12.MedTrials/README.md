# S12.MedTrials - Clinical Trial Operations Copilot

S12.MedTrials demonstrates how Koan's AI and MCP pillars combine to deliver an agent-ready clinical operations service. The sample exposes REST APIs, Model Context Protocol tooling, and an AngularJS single-page app so coordinators can plan participant visits, query protocol guidance, and monitor safety issues with the same orchestrations.

## Highlights

- **Entity-first domain** – `TrialSite`, `ParticipantVisit`, `ProtocolDocument`, `AdverseEventReport`, and `MonitoringNote` are plain Koan entities annotated with `McpEntityAttribute` to expose CRUD tools automatically.
- **AI-powered workflows** – Services reuse the S5 guardrails: embeddings and chat requests resolve via `Ai.TryResolve()`, degrade gracefully when no provider/vector store is configured, and fall back to deterministic heuristics.
- **Planner + digest endpoints** – `POST /participant-visits/plan-adjustments` proposes schedule changes while `POST /adverse-event-reports/summarise` assembles safety digests that agents or humans can review.
- **AngularJS SPA** – Served from the API project's `wwwroot`, mirroring the S10/S11 structure with Bootstrap styling, route-based controllers, and Koan REST integrations.
- **Seeded data** – A hosted worker seeds demo sites, visits, documents, and adverse events so the UI and APIs light up immediately after `dotnet run`.

## Running the sample

```bash
dotnet run --project samples/S12.MedTrials/S12.MedTrials.csproj
```

The API listens on `http://localhost:5090` (see `AppManifest`). Swagger UI and the SPA are hosted by the same project:

- Swagger: `http://localhost:5090/swagger`
- SPA: `http://localhost:5090/index.html`

## Key endpoints

| Method | Endpoint | Description |
| --- | --- | --- |
| `POST` | `/api/protocol-documents/ingest` | Store protocol or monitoring content; generates embeddings when available. |
| `POST` | `/api/protocol-documents/query` | Semantic or deterministic search across stored documents. |
| `POST` | `/api/participant-visits/plan-adjustments` | Runs the visit planner, persists proposed adjustments, and returns diagnostics. |
| `POST` | `/api/adverse-event-reports/summarise` | Produces an adverse-event digest, falling back to deterministic summaries if AI is unavailable. |

## MCP integration

All entities are decorated with `McpEntityAttribute`, so Koan automatically exposes matching Model Context Protocol tools. The planner and digest endpoints reuse the same services that the MCP transport invokes through `EndpointToolExecutor`, ensuring parity between REST clients and IDE/agent tooling. Enable STDIO transport via `appsettings.Development.json` and start Koan as usual to connect MCP-capable agents (e.g., Claude Desktop).

## Configuration

- `appsettings.json` ships Ollama defaults (`all-minilm`) so embeddings can run locally when Ollama is installed.
- `appsettings.Development.json` mirrors the S5 sample: configure MongoDB, Weaviate, and Koan's JWT test provider for local experimentation.
- Vector storage is optional. When `Vector<ProtocolDocument>.IsAvailable` returns `false`, the sample falls back to deterministic search and records diagnostics for MCP clients.

## Next steps

- Extend parity tests to assert that MCP tooling returns the same diagnostics as REST endpoints.
- Add streaming chat UX to surface planner reasoning in the SPA.
- Wire additional AI providers (Azure/OpenAI) through configuration to demonstrate multi-provider routing.
