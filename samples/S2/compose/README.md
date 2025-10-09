# S2 Compose (API + Mongo + Client)

- Prereqs: Docker Desktop
- Start Mongo and API:

```powershell
cd samples/S2.Compose
# Build root image and run api + mongo
docker compose up --build
```

- API default URL: http://localhost:5054
  - Health: GET /api/health
  - Items: GET /api/items, POST /api/items, DELETE /api/items/{id}
  - Bulk: POST /api/items/seed/{count}, DELETE /api/items/clear

- Client URL: http://localhost:5055
  - AngularJS static client served by nginx
  - Proxies /api/* to the API service
  - Proxies /swagger to the API Swagger UI (enabled in compose by default)
  - Seeding: toggle client-side (multiple POSTs) vs server-side (single POST seed/{count}); Clear deletes all

Notes:
- A lightweight probe container checks API health, creates one item, seeds 2 (server-side), clears all, and reads the list on start.

OpenTelemetry (optional):
- The API is wired to emit OTEL traces/metrics when configured. To try locally:
  - A minimal collector config is provided at `otel-collector-config.yaml` that logs received spans/metrics.
  - In `docker-compose.yml`:
    - Uncomment the entire `otel-collector` service section at the bottom.
    - In the `api.environment` block, uncomment `OTEL_EXPORTER_OTLP_ENDPOINT: "http://otel-collector:4317"`.
  - Bring the stack up; the collector will receive exports and print them to its logs (debug exporter).
  - API responses also include a `Koan-Trace-Id` header for quick correlation.

WSL2 tips (optional):
- If using WSL2 with Docker Desktop integration:
  - Run `docker compose` from the repository folder inside WSL for best performance.
  - Ensure your Windows drive is shared with Docker Desktop so the `otel-collector-config.yaml` bind mount works.
  - If you see file-not-found errors for the collector config, verify the relative path and that the file exists in this folder.

Status:
- S2 feature set per spec is implemented end-to-end (API, Client, Compose, Mongo). Integration tests cover health/CRUD and seed/clear.

Config keys:
- ConnectionStrings__Default (mongodb connection string)
- Koan__Data__Mongo__Database (db name)
 - OTEL_EXPORTER_OTLP_ENDPOINT (OTLP gRPC endpoint), OTEL_EXPORTER_OTLP_HEADERS (optional)
