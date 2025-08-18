# S1.Web — Minimal API + AngularJS sample

This sample showcases Sora.Web’s generic EntityController with a tiny AngularJS UI backed by the JSON data adapter.

- Health endpoint: GET /api/health
- CRUD base route: api/todo
- Bulk ops: POST /api/todo/bulk and DELETE /api/todo/bulk
- Clear all: DELETE /api/todo/clear
- Seed: POST /api/todo/seed/{count}
- Pagination headers: X-Total-Count, X-Page, X-Page-Size, X-Total-Pages, plus RFC 5988 Link

## Run locally

Windows (PowerShell or cmd):

- Open a terminal in this folder and run the helper script:

```powershell
# default: http://localhost:5044
./start.bat

# custom URL
./start.bat "http://localhost:5044"
```

This script kills previous instances, frees the port, waits for /api/health, and opens the browser.

Alternatively:

```powershell
# From repo root
dotnet run --project "Sora/samples/S1.Web/S1.Web.csproj" --urls "http://localhost:5044"
```

Then browse to the root (index.html is served via static files). The UI supports CRUD, seeding, and server-side pagination with navigation buttons.

## Quick API tests

Use the included requests.http file (VS Code "REST Client" extension) or copy curl equivalents.

## Container (optional)

Windows helper scripts (from this folder):

```powershell
./start-docker.bat           # Docker (single container); defaults to http://localhost:5044
./start-docker.bat 5090      # Use a different host port

./start-compose.bat          # Docker Compose; reads HOST_PORT from .env (defaults to 5044)
```

Stop and maintenance:

```powershell
./stop-docker.bat            # Stop single-container run
./clean-data.bat             # Remove persisted data folder (danger)
```

Manual Docker:

```powershell
# From repo root
# Use Sora folder as build context so .dockerignore applies
docker build -f Sora/samples/S1.Web/Dockerfile -t sora-s1:latest Sora

docker run --rm -p 5044:5044 -v ${PWD}/Sora/samples/S1.Web/data:/app/data sora-s1:latest
```

Manual Compose:

```powershell
# From S1.Web folder
# Edit .env to change HOST_PORT
HOST_PORT=5081 docker compose up -d --build
```

- App listens on port 5044 inside the container; mapped to your chosen host port.
- Data is persisted to Sora/samples/S1.Web/data/ via a bind mount.

## Notes

- Program.cs is intentionally minimal; Sora.Web self-wires controllers, static files, secure headers, and /api/health via a startup filter. Adjust via `SoraWebOptions`.
- Application policy (ProblemDetails, Rate Limiting) is configured in the app (see `Program.cs`).
- JSON adapter persists under ./data by default; safe for dev.
- For demo pagination, the controller emits headers and Link relations; the UI reads them to enable navigation.
