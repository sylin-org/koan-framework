# S1.Web — Koan Framework Relationship System Demo

This enhanced S1 sample demonstrates the new relationship system in Koan Framework, showcasing instance-based relationship navigation, batch loading, and streaming capabilities, along with a minimal AngularJS UI.

## Entity Relationships

The sample includes a hierarchical entity structure:

```
User (1) ──────┐
               ├──> Todo (N) ──> TodoItem (N)
Category (1) ──┘
```

### Entities & Relationships

- **User**: Represents users who create todos
- **Category**: Organizes todos into categories (Work, Personal, etc.)
- **Todo**: Main todo items with relationships to User and Category
- **TodoItem**: Sub-tasks within a todo

## API Endpoints

### Standard CRUD Operations
- Health endpoint: GET /api/health
- CRUD base routes: /api/todo, /api/users, /api/categories, /api/todoitems
- Bulk ops: POST /api/todo/bulk and DELETE /api/todo/bulk
- Clear all: DELETE /api/todo/clear-all
- Pagination headers: X-Total-Count, X-Page, X-Page-Size, X-Total-Pages, plus RFC 5988 Link

### Relationship System Features
- **Seeding with relationships**: POST /api/todo/seed-with-relationships
- **Relationship demo**: GET /api/todo/relationship-demo/{id}
- **Streaming demo**: GET /api/todo/streaming-demo
- **Entity enrichment**: GET /api/todo?with=all (new RelationshipGraph format)

## Quick Testing Guide

### 1. Setup Data with Relationships
```bash
# Create users
POST /api/users/seed/5

# Create categories
POST /api/categories/seed

# Create interconnected todos and todo items
POST /api/todo/seed-with-relationships
```

### 2. Test New Relationship Features
```bash
# Get enriched todos with RelationshipGraph format
GET /api/todo?with=all

# Comprehensive relationship navigation demo
GET /api/todo/relationship-demo/{id}

# Batch loading and streaming performance demo
GET /api/todo/streaming-demo
```

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
dotnet run --project "Koan/samples/S1.Web/S1.Web.csproj" --urls "http://localhost:5044"
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
# Use Koan folder as build context so .dockerignore applies
docker build -f Koan/samples/S1.Web/Dockerfile -t Koan-s1:latest Koan

docker run --rm -p 5044:5044 -v ${PWD}/Koan/samples/S1.Web/data:/app/data Koan-s1:latest
```

Manual Compose:

```powershell
# From S1.Web folder
# Edit .env to change HOST_PORT
HOST_PORT=5081 docker compose up -d --build
```

- App listens on port 5044 inside the container; mapped to your chosen host port.
- Data is persisted to Koan/samples/S1.Web/data/ via a bind mount.

## Notes

- Program.cs is intentionally minimal; Koan.Web self-wires controllers, static files, secure headers, and /api/health via a startup filter. Adjust via `KoanWebOptions`.
- Application policy (ProblemDetails, Rate Limiting) is configured in the app (see `Program.cs`).
- JSON adapter persists under ./data by default; safe for dev.
- For demo pagination, the controller emits headers and Link relations; the UI reads them to enable navigation.
