# S12.MedTrials - Clinical Trial Operations Copilot

S12.MedTrials demonstrates how Koan's AI and MCP pillars combine to deliver an agent-ready clinical operations service. The sample exposes REST APIs, Model Context Protocol tooling, and an AngularJS single-page app so coordinators can plan participant visits, query protocol guidance, and monitor safety issues with the same orchestrations.

## Highlights

- **Entity-first domain** – `TrialSite`, `ParticipantVisit`, `ProtocolDocument`, `AdverseEventReport`, and `MonitoringNote` are plain Koan entities annotated with `McpEntityAttribute` to expose CRUD tools automatically.
- **AI-powered workflows** – Services use graceful degradation patterns: embeddings and chat requests resolve via `Ai.TryResolve()`, degrade gracefully when no provider/vector store is configured, and fall back to deterministic heuristics.
- **Planner + digest endpoints** – `POST /participant-visits/plan-adjustments` proposes schedule changes while `POST /adverse-event-reports/summarise` assembles safety digests that agents or humans can review.
- **AngularJS SPA** – Served from the API project's `wwwroot` with Bootstrap styling, route-based controllers, and Koan REST integrations.
- **Seeded data** – A hosted worker seeds demo sites, visits, documents, and adverse events so the UI and APIs light up immediately after `dotnet run`.

## Running the sample

### Option 1: Docker Compose (Recommended)

The easiest way to run S12.MedTrials with all dependencies (MongoDB, Weaviate, Ollama):

```bash
# From the S12.MedTrials directory:
.\start.bat
```

This script builds the Docker image, starts all services (API, MongoDB, Weaviate, Ollama), waits for the API to be ready, and opens your browser to `http://localhost:5110`.

**Service Ports:**
- API: `http://localhost:5110`
- MongoDB: `localhost:5111` (mapped to container port 27017)
- Weaviate: `http://localhost:5112` (mapped to container port 8080)
- Ollama: `http://localhost:5113` (mapped to container port 11434)

**Data Persistence:**
All service data is stored in `./data/` subdirectories (mongo, weaviate, ollama-models) for persistence across container restarts.

### Option 2: Local Development

```bash
dotnet run --project samples/S12.MedTrials/S12.MedTrials.csproj
```

The API listens on `http://localhost:5110` (see `AppManifest`). Swagger UI and the SPA are hosted by the same project:

- Swagger: `http://localhost:5110/swagger`
- SPA: `http://localhost:5110/index.html`

**Note:** When running locally, ensure MongoDB, Weaviate, and Ollama are running separately on the ports configured in `appsettings.Development.json`.

## Key endpoints

| Method | Endpoint | Description |
| --- | --- | --- |
| `POST` | `/api/protocol-documents/ingest` | Store protocol or monitoring content; generates embeddings when available. |
| `POST` | `/api/protocol-documents/search` | Semantic or deterministic search across stored documents. |
| `POST` | `/api/participant-visits/plan-adjustments` | Runs the visit planner, persists proposed adjustments, and returns diagnostics. |
| `POST` | `/api/adverse-event-reports/summarise` | Produces an adverse-event digest, falling back to deterministic summaries if AI is unavailable. |

## MCP integration

All entities are decorated with `McpEntityAttribute`, so Koan automatically exposes matching Model Context Protocol tools. The planner and digest endpoints reuse the same services that the MCP transport invokes through `EndpointToolExecutor`, ensuring parity between REST clients and IDE/agent tooling. Enable STDIO transport via `appsettings.Development.json` and start Koan as usual to connect MCP-capable agents (e.g., Claude Desktop).

## Configuration

- `appsettings.json` ships Ollama defaults (`all-minilm`) so embeddings can run locally when Ollama is installed.
- `appsettings.Development.json` configures MongoDB, Weaviate, Ollama, and Koan's JWT test provider for local experimentation.
- Vector storage is optional. When `Vector<ProtocolDocument>.IsAvailable` returns `false`, the sample falls back to deterministic search and records diagnostics for MCP clients.

## Next steps

- Extend parity tests to assert that MCP tooling returns the same diagnostics as REST endpoints.
- Add streaming chat UX to surface planner reasoning in the SPA.
- Wire additional AI providers (Azure/OpenAI) through configuration to demonstrate multi-provider routing.
