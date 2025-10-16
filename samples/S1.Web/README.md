# S1.Web - Koan Framework Relationship System Demo

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

Launch the sample without Docker using the console bootstrap (mirrors `g1c1`):

- **Windows (cmd or PowerShell)**

    ```powershell
    # defaults to http://localhost:4998
    ./start.bat

    # pass additional app arguments (e.g., custom URLs)
    ./start.bat --urls "http://localhost:5055"
    ```

- **PowerShell helper with optional first-argument URL**

    ```powershell
    ./start.ps1                 # http://localhost:4998
    ./start.ps1 "http://localhost:5055"
    ./start.ps1 -AppArgs @('--urls','http://localhost:5055')
    ```

Both scripts set Development environments, spawn `dotnet run --project S1.Web.csproj --no-launch-profile`, and rely on the app lifecycle to open the browser once the server is listening while keeping the app on HTTP by default.

Manual alternative:

```powershell
# From repo root
$env:ASPNETCORE_URLS = "http://localhost:4998"
dotnet run --project "Koan/samples/S1.Web/S1.Web.csproj" --no-launch-profile
```

> If you skip `--no-launch-profile`, the default Visual Studio launch profile will re-enable HTTPS and prompt for the ASP.NET Core developer certificate.

## Quick API tests

Use the included requests.http file (VS Code "REST Client" extension) or copy curl equivalents.

## Container (optional)

Existing Docker helpers remain available when you need containers:

- `./start-docker.bat [hostPort]`
- `./start-compose.bat`
- `./stop-docker.bat`

They still build the Dockerfile in this folder and mount `.Koan/Data` for persistence, but the primary path for day-to-day development is now the console bootstrap above.

## Notes

- Program.cs now mirrors the g1c1 console bootstrap: simple logging, lifecycle hooks, and browser launch once the app is ready.
- Application policy (ProblemDetails, Rate Limiting) is configured in the app (see `Program.cs`).
- JSON adapter persists under ./data by default; safe for dev.
- For demo pagination, the controller emits headers and Link relations; the UI reads them to enable navigation.
