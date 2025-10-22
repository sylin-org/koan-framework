# S16.PantryPal — Sample Family

Components

- API/ — PantryPal HTTP API (controllers, models, services)
- MCP/ — Move target for MCP host (previously S16.PantryPal.McpHost)

Run

- API: open `API/` and run `start.bat`.

Notes

- Uses Entity<T> patterns and controllers per Koan conventions.
- See family-level docs under `docs/engineering/samples-organization.md` for layout details.# S16 PantryPal: AI‑Powered Meal Planning

Vision-based pantry management + intelligent meal suggestions + MCP Code Mode orchestration.

PantryPal demonstrates Koan’s entity-first patterns with practical AI:

- Computer Vision: Detect grocery items from photos with bounding boxes and confidences
- Natural Language: Parse inputs like “5 lbs, expires in a week” into structured data
- Personalization: Improve recipe suggestions from user feedback
- Orchestration: MCP Code Mode composes multi-entity workflows in a single execution

## Unified Frontend (AngularJS + Tailwind) SPA

Single SPA under `wwwroot/` (no `mockup/` directory). All templates, controllers, directives, and service-layer abstractions are first-class:

- Angular module: `wwwroot/js/app.js` (hash routing via `ngRoute`)
- Controllers: `wwwroot/js/controllers.js` (navigation, pantry, capture → review → confirm, insights, meals)
- Components: `wwwroot/js/components.js` (cards, drawers, detection grid, toasts)
- Service layer: `wwwroot/js/api.js` (in-memory simulation; easy swap to HTTP clients)
- Templates: `wwwroot/templates/*.html` (dashboard, pantry, capture, review, confirm, meals, shopping, insights)
- Styling: Tailwind CDN (Calm Utility: neutral bg, subtle elevation, primary accents)

Rationale

1. One authoritative SPA (no redirects/drift) 2) No duplicate resources 3) Clear path to real HTTP by swapping `api.js` internals 4) Mobile-first layout, desktop density where appropriate

Developer Notes

- Local state is namespaced (e.g., `pantry_items_v1`) for safe version bumps
- Capture workflow state machine: `camera → review → confirm`
- Bounding box rendering and per-candidate selection are UI-only; server reflects production ingestion shape
- Heavy logic (aggregation, AI simulation) is isolated in services for future replacement with real APIs

Edge Cases (handled gracefully)

- Empty pantry and insights zero-states
- Large inventories (responsive grid; infinite scroll; desktop pager)
- Camera permission denied (upload fallback)
- Degraded semantic search: shows “Semantic offline — using lexical” chip when applicable
- Mobile bottom nav + FAB; desktop top nav and optional left rail

## Quick Start

dotnet run --project samples/S16.PantryPal

```bash
# From repository root (API only for quick iteration)
dotnet run --project samples/S16.PantryPal

# Recommended: run split stack (API + dedicated MCP host + Mongo + optional Ollama)
./samples/S16.PantryPal/start.bat
```

The stack (split architecture) starts with:

- API service on 5016 (REST controllers)
- MCP host on 5026 (MCP HTTP/SSE + Code Mode + SDK definitions)
- Mongo as primary store; optional Ollama (vision model auto-discovery)
- 50+ seeded recipes; sample pantry items; demo profile
- Mock vision service enabled by default

Access / Endpoints:

- **API (Swagger)**: http://localhost:5016/swagger
- **MCP SDK (.d.ts)**: http://localhost:5026/mcp/sdk/definitions
- **MCP SSE Base**: http://localhost:5026/mcp
- **API Health**: http://localhost:5016/healthz (if exposed)
- **MCP Health**: http://localhost:5026/healthz

### Architecture Note (Embedded vs Split)

Historically this sample co-hosted MCP and REST in one process. It now demonstrates production-aligned separation:

| Concern               | Split Benefit                                           |
| --------------------- | ------------------------------------------------------- |
| Resource Isolation    | Code Mode CPU/memory spikes do not degrade REST latency |
| Scaling               | Independent horizontal scaling & autoscaling policies   |
| Security Blast Radius | Script sandbox faults cannot crash API process          |
| Deployment Cadence    | Upgrade MCP runtime without redeploying controllers     |
| Observability         | Clear service-level metrics & logs                      |
| Quota Tuning          | Different CPU ms / memory ceilings per service          |

For minimal demos you can still run only the API project (MCP disabled). For teaching best practices we keep the split default.

### Code Mode: Fetch SDK & Execute Script

Fetch generated TypeScript definition (includes integrity footer):

```bash
curl -s http://localhost:5026/mcp/sdk/definitions > koan-sdk.d.ts
tail -n 3 koan-sdk.d.ts  # shows integrity-sha256 footer
```

List tools (Stream JSON-RPC over HTTP SSE negotiated internally – simplified example using POST tools/list):

```bash
curl -s -X POST http://localhost:5026/mcp/rpc \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"1","method":"tools/list"}' | jq
```

Execute a Code Mode script that returns top 3 recipe names:

```bash
cat <<'EOF' > script.js
const recipes = SDK.Entities.Recipe.collection({ pageSize: 3 });
SDK.Out.answer(JSON.stringify(recipes.items.map(r => r.name)));
EOF

curl -s -X POST http://localhost:5026/mcp/rpc \
  -H "Content-Type: application/json" \
  -d @<(echo '{"jsonrpc":"2.0","id":"2","method":"tools/call","params":{"name":"koan.code.execute","arguments":{"code":'"'$(jq -Rs . < script.js)'"'}}}') | jq
```

Expected response fragment:

```json
{
  "jsonrpc": "2.0",
  "id": "2",
  "result": {
    "content": [
      { "type": "text", "text": "[\"Classic Spaghetti Carbonara\", ...]" }
    ]
  }
}
```

### Development Summary

| Scenario                              | Command                                                            |
| ------------------------------------- | ------------------------------------------------------------------ |
| Run split stack                       | `./samples/S16.PantryPal/start.bat`                                |
| Only API (quick iterate)              | `dotnet run --project samples/S16.PantryPal`                       |
| Rebuild MCP host                      | `dotnet run --project samples/S16.PantryPal.McpHost`               |
| Fetch SDK (watch diff)                | `curl -s http://localhost:5026/mcp/sdk/definitions > sdk.d.ts`     |
| Run unit tests (services + ingestion) | `dotnet test tests/S16.PantryPal.Tests/S16.PantryPal.Tests.csproj` |

> Integrity footer enables detecting generation drift & tampering; hash covers the content segment prior to the footer line.

## Core Features

### 1) Vision-Powered Pantry Management (api/action)

Upload grocery photos → AI detects items with bounding boxes → Confirm and add to pantry

Photo persistence now uses a thin abstraction (`IPhotoStorage`) over Koan.Storage. This decouples ingestion from physical filesystem paths and enables future swap to S3 / Azure Blob / cold-tier providers without controller changes. Configure via:

```json
// appsettings.json (optional override of defaults)
{
  "S16": {
    "Photos": {
      "Profile": "", // blank -> use storage DefaultProfile
      "Container": "pantry-photos",
      "Prefix": "photos/" // object key namespace
    }
  }
}
```

Storage profile/container resolution follows Koan.Storage rules (DefaultProfile or single-profile fallback). Uploaded photo keys are GUID v7 (time-orderable) prefixed with `photos/`.

> Note: Thumbnail generation was intentionally removed to keep the sample lean and focused on vision + orchestration concepts. Re-introduce via an image pipeline (resizer service + background job) if demonstrating media processing patterns.

Example consolidated storage section (current sample default):

```json
{
  "Koan:Storage": {
    "Profiles": {
      "photos": { "Provider": "local", "Container": "pantry-photos" }
    },
    "DefaultProfile": "photos",
    "FallbackMode": "SingleProfileOnly"
  },
  "S16": {
    "Photos": {
      "Profile": "photos",
      "Container": "pantry-photos",
      "Prefix": "photos/"
    }
  }
}
```

Hybrid Search via q= (entity-first): Add `Koan.Data.Vector` provider configuration (pgvector/in-memory) to enable semantic+lexical blending when using `q=` on data endpoints. If unavailable, responses include `X-Search-Degraded: 1` and the UI shows a chip.

Multi-Candidate Detection: AI provides top 3 alternatives for each item, user selects the correct one.

```http
POST /api/action/pantry/upload
Content-Type: multipart/form-data

photo=@groceries.jpg
```

**Response**: Detections with bounding boxes and confidence scores

```json
{
  "photoId": "abc123",
  "detections": [
    {
      "boundingBox": { "x": 50, "y": 100, "width": 200, "height": 150 },
      "candidates": [
        { "name": "chicken breast", "confidence": 0.95 },
        { "name": "chicken thigh", "confidence": 0.78 }
      ]
    }
  ]
}
```

### 2) Natural Language Input Parsing

Flexible quantity and expiration parsing:

- **ISO dates**: `"2025-10-15"`
- **Relative**: `"in 3 days"`, `"next month"`, `"tomorrow"`
- **Month names**: `"March 15"`, `"Oct 10"`
- **Fractions**: `"1/2 cup"`, `"3 1/2 lbs"`
- **Flexible units**: `"lb"`, `"pounds"`, `"can"`, `"jar"`, `"whole"`

```http
POST /api/action/pantry/confirm/{photoId}
{
  "confirmations": [
    {
      "detectionId": "det1",
      "selectedCandidateId": "cand1",
      "userInput": "2 lbs, expires next week"
    }
  ]
}
```

Parser extracts:

```json
{
  "quantity": 2,
  "unit": "lbs",
  "expiresAt": "2025-10-14T00:00:00Z",
  "confidence": "High"
}
```

### 3) Intelligent Meal Suggestions

AI suggests recipes based on:

- Available pantry items
- Dietary restrictions
- Cooking time constraints
- User ratings and history

```http
POST /api/meals/suggest
{
  "userId": "user1",
  "dietaryRestrictions": ["vegetarian"],
  "maxCookingMinutes": 45
}
```

**Response**: Scored recipes with availability analysis

```json
{
  "recipe": {
    "name": "Penne Arrabbiata",
    "availabilityScore": 0.85,
    "missingIngredients": ["red chili flakes"]
  },
  "score": 0.78
}
```

### 4) MCP Code Mode Orchestration

Complex workflows execute in single roundtrip:

**Example: Photo Upload → Inventory Update → Recipe Suggestions**

```javascript
// MCP Code Mode script
const photo = SDK.Entities.PantryPhoto.getById(photoId);
const pantry = SDK.Entities.PantryItem.collection();

// Process each detection
photo.detections.forEach((detection) => {
  const item = detection.candidates[0]; // Top AI pick

  // Check for duplicates
  const existing = pantry.items.find(
    (p) => p.name.toLowerCase() === item.name.toLowerCase()
  );

  if (existing) {
    // Update quantity
    SDK.Entities.PantryItem.upsert({
      id: existing.id,
      quantity: existing.quantity + 1,
    });
  } else {
    // Create new item
    SDK.Entities.PantryItem.upsert({
      name: item.name,
      category: item.category,
      quantity: 1,
      unit: item.defaultUnit,
      status: "available",
    });
  }
});

// Get updated pantry
const updated = SDK.Entities.PantryItem.collection();

// Suggest recipes using new items
const recipes = SDK.Entities.Recipe.collection({
  pageSize: 5,
});

SDK.Out.answer(
  JSON.stringify({
    itemsAdded: photo.detections.length,
    totalPantryItems: updated.totalCount,
    suggestedRecipes: recipes.items.map((r) => r.name),
  })
);
```

**Traditional MCP**: 10+ roundtrips (get photo, check duplicates per item, upsert items, get pantry, get recipes)

**Code Mode**: 1 roundtrip with full logic

### 5) Search (q= on api/data)

Use `q=` on entity data endpoints (no custom `/pantry/search`). Providers may blend semantic + lexical. When vectors are unavailable, responses include `X-Search-Degraded: 1` and the UI surfaces a “Semantic offline — using lexical” chip.

## Entity Model

```csharp
[McpEntity] Recipe           // 50+ recipes with nutrition, instructions
[McpEntity] PantryItem       // Inventory with vision metadata
[McpEntity] MealPlan         // Scheduled meals with user feedback
[McpEntity] ShoppingList     // Auto-generated from meal plans
[McpEntity] UserProfile      // Preferences, goals, dietary restrictions
[McpEntity] PantryPhoto      // Photos with AI detection results
[McpEntity] VisionSettings   // User vision preferences
```

All entities support:

- CRUD via `EntityController<T>`
- MCP Code Mode via `SDK.Entities.{Name}`
- Multi-tenant routing via `set`
- Relationship expansion via `with` (with `*` allowed; use judiciously)

## API Endpoints

### Data (Entity-First)

- `GET /api/data/pantry?filter={...}&q=&page=&pageSize=&sort=&with=&view=`
- Same for: `recipes`, `mealplans`, `shopping`, `profiles`, `photos`, `visionsettings`

Notes:

- Default `pageSize=50` across the app (UI omits a selector; desktop shows pager when filters are active).
- Use `q=` for hybrid search signals; degraded vector sets `X-Search-Degraded: 1` on responses.
- Prefer minimal `with` on listings; use `with=*` only for developer scenarios.
- For small, targeted edits use `PATCH /api/data/{model}/{id}` (partial update). Pantry qty/unit steppers are optimistic and PATCH partial fields.

### Actions (Verbs)

- `POST /api/action/pantry/upload` — Upload photo (multipart) for vision processing
- `POST /api/action/pantry/confirm/{photoId}` — Confirm detections, add to pantry
- `GET  /api/pantry-insights/stats` — Pantry statistics and insights (read-only)

### Meal Planning

- `POST /api/meals/suggest` - Get recipe suggestions
- `POST /api/meals/plan` - Create multi-day meal plan
- `POST /api/meals/shopping/{planId}` - Generate shopping list

### Entity Controller Surface

- Common params: `page`, `pageSize`, `all=true`, `filter={...}`, `q=`, `sort=Field,-OtherField`, `with=relatedEntity`, `view=compact`, `output=dict`
- Writes: prefer `PATCH` for partial updates; `POST` for create (full object)

## MCP Code Mode Scripts

Located in `Scripts/` folder:

1. **simple-dinner.js** - Basic meal suggestion
2. **smart-suggest.js** - Context-aware with waste reduction
3. **week-planning.js** - Week planning with optimization
4. **meal-prep.js** - Sunday meal prep timeline
5. **grocery-haul.js** - Photo processing workflow

Execute via:

```http
POST /mcp/rpc
{
  "method": "tools/call",
  "params": {
    "name": "koan.code.execute",
    "arguments": {
      "code": "/* script content */"
    }
  }
}
```

## Vision Processing Pipeline

```
Photo Upload
    ↓
AI Detection (Ollama llava / Mock)
    ↓
Bounding Box Generation
    ↓
Multi-Candidate Results (top 3 per item)
    ↓
User Selection & Natural Language Input
    ↓
Parser → Structured Data
    ↓
Pantry Item Created
```

Mock Vision Service (default): realistic detections without AI infra

Production: Replace with `OllamaVisionService` or `OpenAIVisionService`

```csharp
// In Initialization/KoanAutoRegistrar.cs
services.AddSingleton<IPantryVisionService, OllamaVisionService>();
```

## Configuration

### Vision Service

```json
{
  "Vision": {
    "Provider": "mock", // or "ollama", "openai"
    "MinConfidence": 0.5,
    "MaxDetectionsPerPhoto": 20
  }
}
```

### MCP Code Mode

```json
{
  "Koan": {
    "Mcp": {
      "CodeMode": {
        "Enabled": true,
        "CpuMilliseconds": 3000,
        "MemoryMegabytes": 128
      }
    }
  }
}
```

## Progressive Complexity

**Level 1: Basic** - Manual pantry, simple suggestions

**Level 2: Vision** - Photo upload, AI detection, confirmation UI

**Level 3: Smart** - Context-aware suggestions (expiring items, macros)

**Level 4: Planning** - Week planning with budget/nutrition optimization

**Level 5: Advanced** - Meal prep workflows, batch cooking

## Development Tips

### Ingestion Hardening Summary

Recent enhancements:

- File validation: size + extension whitelist
- Duplicate suppression per photo (detections already confirmed ignored)
- Shelf-life inference: category-based default expiration (`S16:Ingestion`)
- Structured error responses (400 validation / 404 missing photo)

`IngestionOptions` (env prefix `S16__Ingestion__`):

```json
{
  "S16": {
    "Ingestion": {
      "MaxUploadBytes": 5242880,
      "AllowedExtensions": [".jpg", ".jpeg", ".png", ".webp"],
      "DefaultShelfLifeDaysByCategory": {
        "produce": 5,
        "dairy": 7,
        "bakery": 3,
        "meat": 3
      }
    }
  }
}
```

### Flight-Once Seeding

`PantrySeedHostedService` inserts baseline pantry items if store empty at boot; idempotent and safe under concurrency.

### Container & Compose

Build and run with the provided multi-stage `Dockerfile` and compose file:

```bash
docker compose -f samples/S16.PantryPal/docker/docker-compose.yml up --build
```

Service: http://localhost:8080

### MVP Operational Checklist

- [ ] Pagination default (50) & reasonable clamp
- [ ] Ingestion upload + confirm creates items
- [ ] Duplicate detections ignored
- [ ] Shelf-life inferred when missing expiration
- [ ] Search returns items & sets `X-Search-Degraded` when degraded
- [ ] Meal suggestions produce scored results
- [ ] Seed ran once (subsequent boots skip)
- [ ] Container responds on 8080

### Adding New Recipes

```csharp
// In Data/RecipeSeedData.cs
new Recipe
{
    Name = "Your Recipe",
    Cuisines = new[] { "Italian" },
    PrepTimeMinutes = 20,
    Ingredients = new[] {
        new RecipeIngredient { Name = "pasta", Amount = 1, Unit = "lbs" }
    },
    Steps = new[] { "Step 1...", "Step 2..." }
}
```

### Custom Vision Provider

```csharp
public class CustomVisionService : IPantryVisionService
{
    public async Task<VisionProcessingResult> ProcessPhotoAsync(...)
    {
        // Your AI logic here
        return new VisionProcessingResult
        {
            Success = true,
            Detections = detections,
            ProcessingTimeMs = elapsedMs
        };
    }
}
```

### Natural Language Parser Extensions

```csharp
// Extend PantryInputParser for new patterns
private bool TryParseCustomFormat(string input, out ParsedItemData result)
{
    // Add your parsing logic
}
```

## Architecture Highlights

**Entity-First Development**: `Todo.Get(id)`, `todo.Save()` patterns

**Multi-Provider Ready**: SQLite → Postgres/MongoDB with zero code changes

**Auto-Registration**: Adding package reference automatically enables features

**Self-Reporting**: Bootstrap reports show provider elections and capabilities

See `TECHNICAL.md` for deeper layering and extension guidance. For the full UI/UX and AngularJS binding details, see `UI-SPEC.md` in this folder.

## Testing

Focused unit tests cover service orchestration logic and ingestion flow (direct controller invocation with fakes).

```bash
dotnet test tests/S16.PantryPal.Tests/S16.PantryPal.Tests.csproj -c Debug
```

Add integration tests later for full HTTP + persistence provider scenarios.

## Related Samples

- **S12.MedTrials**: Traditional MCP entity tools
- **S13.DocMind**: Vector search + AI integration
- **S14.AdapterBench**: Multi-provider benchmarking

## Related Decisions

- **AI-0014**: MCP Code Mode - Technical foundation
- **DATA-0078**: Vector Export for Migration
- **SAMPLE-0016**: This sample's architecture decision

## Recent Decisions & Roadmap (2025-10-07)

### Auth & User Experience

- Test Auth is always enabled (see S5.Recs). If no user is signed in, the UI is browse-only. Write actions (upload, confirm, qty edits) are disabled.
- Multi-user supported; multi-tenant not in scope for this sample.

### Batch Operations

- Batch operations (e.g., photo detection, pantry updates) accept partial success. Failures are reported per item.

### Vision/Model Extensibility

- Vision provider selection is extensible (see Ollama adapter, DocMind for multi-model usage).
- Entity relationships (e.g., ingredient substitutions, user preferences) are being added for richer scenarios.

### Media Pipeline

- Detected ingredient images are cropped and saved locally using the Storage pipeline for visual reference.

### Pagination & Streaming

- All in-memory entity queries are being refactored: web UI uses paging, backend uses streaming for large data.

### Documentation

- "Behind the scenes" docs are provided both as in-code (g1c1-style) comments and as comprehensive in-folder documentation for developers.

### Testing & Roadmap

- Advanced/integration tests are targeted for v1.1.
- Production goal: pilot/fully functional prototype, mobile-first for photos, no regulatory compliance required.

For architectural rationale and more, see `/docs/decisions/SAMPLE-0016-kitchenmind-mcp-ai-showcase.md`.

## Agentic AI + Code-Mode MCP Integration: Findings & Proposals (2025-10-07)

### Findings

- The value of S16 is maximized when the AI pipeline can reason over, discover, and orchestrate workflows using a code-enabled MCP service.
- Current Koan.Mcp exposes entities and tools as discrete operations, but does not yet provide a TypeScript/JS code-mode surface for agentic AI.
- The code-mode pattern (see Cloudflare, OpenAI, Anthropic) enables the agent to compose multi-step workflows in a single script, reducing roundtrips and improving reliability.
- Koan.Mcp’s zero-config, entity-first design is a strong foundation for this, but needs a code-mode execution endpoint, SDK surface, and capability registry.

### Proposals

1. **Enhance Koan.Mcp with Code-Mode Support:**

- Add an `executeCode` endpoint that runs agent-authored TS/JS code in a secure sandbox, with access to a minimal, typed SDK (Entities, Out, etc.).
- Auto-generate the SDK surface from registered entities/tools, and expose it as a `.d.ts` for agent prompt context.
- Expose a machine-readable capability registry for agentic discovery.
- Enforce quotas, audit, and validate all code runs for safety and observability.

2. **Integrate S16 with Code-Mode MCP:**

- Register all S16 entities and workflows as MCP tools.
- The AI pipeline queries the MCP registry, receives the SDK, and crafts code-mode scripts to fulfill user intents (e.g., meal planning, pantry updates).
- Demo multi-step workflows as single code-mode scripts, showing agentic reasoning and orchestration.

3. **DX & Documentation:**

- Document the code-mode surface, SDK types, and example scripts in S16 and Koan.Mcp docs.
- Provide developer samples and golden scenarios for testing and validation.

### Opportunities for Koan.Mcp

- Become a reference implementation for agentic, code-enabled orchestration in modern AI systems.
- Maintain Koan’s “just works” DX: zero-config, strong defaults, and extensibility.
- Lead in security, observability, and developer experience for code-mode MCP.

## License

Part of Koan Framework - see root LICENSE file
