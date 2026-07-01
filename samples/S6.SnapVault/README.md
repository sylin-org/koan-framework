# SnapVault Pro - Complete Photo Management System

**A production-ready photo management application showcasing Koan Framework's full capabilities: media processing, AI vision, semantic search, and modern web UX.**

![Status](https://img.shields.io/badge/status-production--ready-success)
![Framework](https://img.shields.io/badge/koan-v0.6.3-blue)
![Backend](https://img.shields.io/badge/backend-ASP.NET_Core-purple)
![Frontend](https://img.shields.io/badge/frontend-vanilla_js-yellow)

---

## ✨ Greenfield rebuild on Koan

SnapVault was **rebuilt greenfield** on the current Koan Framework — a clean Koan-native backend, the domain ported
verbatim, the legacy backend deleted in one swap. Versus that legacy backend, on the comparable surface: **−41% LOC**,
**−604 lines of bespoke plumbing** (media controller, monitoring service, cascade config) eliminated to zero,
**−4 media-derivative entity types** (now three one-line `[MediaRecipe]`s), and **−2 third-party NuGet packages**
(EXIF → ImageSharp, progress → SSE) — while *adding* an entire studio↔client guest lifecycle, fail-closed data-layer
access (SEC-0008), and SSE progress. The framework absorbed the plumbing (Reference = Intent).

**See**: [snapvault-product-spec.md](../../docs/architecture/snapvault-product-spec.md) for the build shape + the full measurement.

---

## What is SnapVault?

SnapVault is a **self-hosted photo management system** built with Koan Framework that rivals commercial solutions like Google Photos. It's not a toy example—it's a complete, production-ready application demonstrating:

✨ **AI Vision Analysis** - Vision models analyze photos and generate structured metadata (tags, summaries, compositional details)
🔍 **Semantic Search** - Natural language queries like "sunset at beach" or "people laughing"
🎨 **Modern Dark UI** - Professional gallery with keyboard shortcuts, accessibility, and responsive design
⚡ **Smart Processing** - On-demand `[MediaRecipe]` renders (gallery/masonry/retina) served + cached from the single original, no pre-generated derivative entities
📊 **Real-time Progress** - Server-Sent Events stream upload progress (a projection of the durable jobs ledger; no SignalR)
💾 **Provider-Swappable Storage** - One stored original; `cold` profile binds to Local (dev) or S3 (prod) by config alone
📱 **Complete Features** - Favorites, ratings, bulk actions, drag-drop upload, event organization, timeline views

## Quick Start

**Prerequisites**: Docker Desktop (manages MongoDB, Weaviate, Ollama automatically)

```bash
# Clone the repository
git clone https://github.com/your-org/koan-framework.git
cd koan-framework/samples/S6.SnapVault

# Run the start script (handles everything automatically)
./start.bat  # Windows
# or
./start.sh   # macOS/Linux
```

The script automatically:
1. Builds the Docker image
2. Starts all required services (MongoDB, Weaviate, Ollama)
3. Initializes the application
4. Opens your browser to http://localhost:5086

**That's it!** Upload photos, try semantic search, explore the lightbox.

---

## What You'll Learn

This sample demonstrates **production-ready Koan Framework patterns**:

### 🖼️ Media Processing — declarative `[MediaRecipe]`

Renders are **declared, not stored**. The framework serves `GET /media/{id}/{recipe}`, rendering (and caching) on
demand from the single stored original — no derivative entities, no pre-generation, no bespoke controller:

```csharp
// Media/PhotoRecipes.cs — three one-line declarations replace 4 derivative entity types + a reflection hack
[MediaRecipe("gallery", Description = "1200px web view, JPEG")]
public static MediaRecipe Gallery() => MediaRecipe.New().ResizeFit(1200, 1200).EncodeAs("jpeg");

[MediaRecipe("masonry", Description = "300px masonry grid tile, JPEG")]
public static MediaRecipe Masonry() => MediaRecipe.New().ResizeFit(300, 300).EncodeAs("jpeg");
```

**Why it matters**: serving inherits the SEC-0008 access axis + tenancy *structurally* (via `MediaEntitySource<T>`),
and there is nothing to keep in sync — the original is the only blob.

### 🤖 AI Integration (Vision + Embeddings) - NEW AI-0020 Patterns!

**Declarative embedding generation with attribute-driven lifecycle**:

```csharp
// 1. Add [Embedding] attribute to entity
[Embedding(
    Policy = EmbeddingPolicy.Explicit,
    Async = true,            // Background queue processing
    MaxTokens = 8191,        // Auto-truncation with warnings
    Version = 1)]            // Schema versioning
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string ToEmbeddingText()  // Framework calls automatically
    {
        // Build searchable text from AI analysis + metadata
        return $"{AiAnalysis.ToEmbeddingText()}\n{OriginalFileName}...";
    }
}

// 2. Vision analysis — model auto-selected by ZenGarden advisor
var analysisJson = await Client.Chat(structuredJsonPrompt, new ChatOptions
{
    Image = imageBytes,
    ImageMimeType = "image/jpeg"
}, ct);

// 3. Save entity - framework handles embedding automatically!
photo.ProcessingStatus = ProcessingStatus.Completed;
await photo.Save(ct);  // ✨ Embedding + vectorization automatic

// 4. Semantic search (hybrid: keyword + vector)
var results = await Vector<PhotoAsset>.Search(
    vector: queryEmbedding,
    text: query,
    alpha: 0.5,  // 50% semantic, 50% keyword
    topK: 20
);
```

**Why it matters**:
- ✅ **62.5% code reduction** (400 → 150 lines) via declarative patterns
- ✅ **Transaction safety** prevents orphaned vectors (atomic commits)
- ✅ **Production monitoring** tracks cost, success rate, latency
- ✅ **Async queue** handles high-volume scenarios (100+ photos)
- ✅ **Auto-truncation** respects token limits with developer warnings

### 💾 Single-Original Storage + On-Demand Renders

The greenfield collapsed the legacy hot/warm/cold derivative tiers into **one stored original** plus
**on-demand `[MediaRecipe]` renders** — no `PhotoGallery`/`PhotoThumbnail`/`PhotoMasonry`/`PhotoRetina`
entities, no pre-generation, no bespoke media controller:

```csharp
// The one stored original — the only blob that persists.
[StorageBinding(Profile = "cold", Container = "photos")]
public class PhotoAsset : MediaEntity<PhotoAsset> { }

// Renders are declared, not stored: the framework serves GET /media/{id}/{recipe}, rendering (and
// caching) on demand from the single original — access-scoped via MediaEntitySource<T>.
[MediaRecipe("gallery")] static MediaRecipe Gallery() => Recipe().ResizeFit(1200, 1200).EncodeAs("jpeg");
```

**Storage is provider-swappable by config alone** (multi-provider transparency — zero entity/code change):
`appsettings.json` binds the `cold` profile to `Local` for dev (`./storage`); `appsettings.Production.json`
binds the *same* `cold` profile to `S3` for prod. S3 credentials come from the environment (never committed):
`Koan__Storage__Providers__S3__Endpoint`, `Koan__Storage__Providers__S3__AccessKey`, `…__SecretKey`.

### 📡 Real-Time Progress Tracking

Upload progress is a **read-projection of the durable jobs ledger**, streamed as Server-Sent Events — no
SignalR hub, no groups, no client library. The processing job reports progress once, durably, via `ctx.Progress`;
the SSE endpoint tails the ledger:

```csharp
// Backend: the job reports durable progress (persisted to the ledger, read by the SSE projection)
await ctx.Progress(0.4, "ai-description");  // upload → exif → thumbnails → ai-description → completed

// The endpoint streams the batch's progress straight off the ledger (Controllers/UploadProgressController.cs)
[HttpGet("progress/{batchId}")]
public IActionResult Progress(string batchId)
    => SseActionResult.StreamEnvelopes(UploadProgressProjection.StreamAsync(batchId, HttpContext.RequestAborted));
```

```javascript
// Frontend: the browser-native EventSource (no library) listens and updates the UI
const source = new EventSource(`/api/photos/progress/${batchId}`);
source.addEventListener('PhotoProgress', (e) => updateProgressCard(JSON.parse(e.data)));
source.addEventListener('JobCompleted', (e) => { /* ...*/ source.close(); });
```

**Why it matters**: you already wrote a durable, tenant-carried job — progress streaming is a projection of it,
not a second broadcast system to build and keep in sync.

### 🎨 Modern Web Application

Production-quality frontend with vanilla JavaScript components:

- **Photo Grid**: Masonry layout with lazy loading, selection, quick actions
- **Lightbox**: Full-screen viewer with zoom, pan, keyboard navigation, accessibility
- **Unified Panel**: EXIF metadata, AI insights, and actions in one slide-out panel
- **Upload Modal**: Drag-drop with event selection and progress tracking
- **Search Bar**: Hybrid search with alpha slider (keyword ↔ semantic)
- **Keyboard Shortcuts**: Full navigation without mouse
- **Accessibility**: ARIA labels, focus management, screen reader support

**Why vanilla JS?** This sample focuses on backend patterns. Simple frontend means less learning curve.

---

## Architecture Highlights

### Entity-First Development

**Traditional approach**:
```csharp
// Repositories, services, layers of abstraction
public class PhotoRepository : IPhotoRepository { /* ... */ }
public class PhotoService : IPhotoService { /* ... */ }
```

**Koan approach**:
```csharp
// Entities are self-sufficient
var photo = await PhotoAsset.Get(id);
await photo.Save();
```

**Result**: Less infrastructure code, more focus on domain logic.

### Automatic API Generation

```csharp
// Inherit EntityController for automatic CRUD endpoints
[Route("api/[controller]")]
public class PhotosController : EntityController<PhotoAsset>
{
    // GET, POST, PUT, DELETE provided automatically

    // Add custom endpoints for business logic
    [HttpPost("upload")]
    public async Task<ActionResult> Upload(...) { }
}
```

### Background Processing Pattern

Durable, tenant-carrying background work via `Koan.Jobs` — no in-memory queue, no fire-and-forget `Task.Run`
(which escaped both retries and the ambient tenant), no separate batch-tracker entity:

```csharp
// 1. Submit one durable job per file, sharing a batch id (the ambient tenant rides across the async hop).
var jobs = files.Select(f => new PhotoProcessingJob { BatchJobId = batchId, OriginalFileName = f.Name });
await jobs.Submit(PhotoProcessingJob.Ingest);

// 2. Return the batch id immediately; the browser opens an EventSource on it.
return Ok(new { jobId = batchId, totalQueued = files.Count });

// 3. The job's handler runs the pipeline and reports progress durably (read by the SSE projection).
public static async Task Execute(PhotoProcessingJob job, JobContext ctx, CancellationToken ct) { /* ctx.Progress(...) */ }
```

---

## Project Structure

```
samples/S6.SnapVault/
├── Controllers/                 # Thin actions over Entity<T> / EntityController<T>
│   ├── PhotosController.cs      # EntityController<PhotoAsset> + upload/actions/locks (raw write verbs sealed)
│   ├── EventsController.cs      # EntityController<Event>  (list/create, [Pagination(Mode=Off)])
│   ├── CollectionsController.cs # EntityController<Collection> + rename/add(capped)/remove
│   ├── PhotoSetsController.cs   # POST /query — the windowed grid (all-photos/favorites/collection/search/event)
│   ├── AnalysisStylesController.cs
│   ├── MaintenanceController.cs # stats + wipe (operator-only)
│   ├── GalleryController.cs     # studio↔client: invite → accept (operator/guest)
│   ├── ProofingController.cs    # guest-scoped select/rate/comment (the guest-write floor)
│   └── UploadProgressController.cs # SSE progress projection
├── Services/                    # Ported §3 domain + the guest lifecycle (thin, no repositories)
│   ├── PhotoProcessingService.cs   # ingest: storage → EXIF → daily-event → AI → embedding
│   ├── PhotoSetService.cs          # the windowing/sort engine
│   ├── AI/AnalysisPromptFactory.cs # 15 styles + smart classification + reroll-with-holds
│   └── Lifecycle/                  # GalleryInvite/GuestScope/Proofing/Deprovisioning services
├── Models/
│   ├── PhotoAsset.cs            # the stored original ([StorageBinding cold], [AccessScoped], [Embedding])
│   ├── Event.cs / Collection.cs / AnalysisStyle.cs / PhotoSetSession.cs / AiAnalysis.cs
│   ├── PhotoProcessingJob.cs / UploadStaging.cs   # durable tenant-carrying ingest
│   └── Lifecycle/              # GalleryInvite/GalleryGrant/ProofSelection/ClientErasureCertificate
├── Media/PhotoRecipes.cs        # the 3 [MediaRecipe] declarations (gallery/masonry/retina)
├── Progress/                    # Upload progress = an SSE read-projection of the jobs ledger (no SignalR hub)
│   ├── PhotoProgressContract.cs
│   └── UploadProgressProjection.cs
├── Initialization/
│   ├── SnapVaultModule.cs       # KoanModule — DI + boot (replaces the old KoanAutoRegistrar)
│   ├── SnapVaultSubjectMiddleware.cs  # operator/guest fail-closed subject resolution (SEC-0008)
│   ├── OperatorOnlyAttribute.cs / PhotoAssetCleanup.cs
│   └── AnalysisStyleSeeder.cs
├── wwwroot/
│   ├── js/
│   │   ├── app.js               # Main application
│   │   ├── components/          # UI components (grid, lightbox, upload, etc.)
│   │   └── api.js               # Fetch wrapper
│   └── css/
│       ├── app.css              # Main styles
│       └── lightbox*.css        # Lightbox-specific styles
├── docs/
│   ├── LIGHTBOX_OVERVIEW.md     # Lightbox architecture
│   ├── LIGHTBOX_TECHNICAL_REFERENCE.md  # Implementation details
│   ├── UX-DESIGN-SPECIFICATION.md       # Design system
│   └── archive/                 # Development history
├── DEVELOPER_GUIDE.md           # 👈 START HERE for deep dive
├── README.md                    # This file
└── start.bat / start.sh         # One-command startup
```

---

## Features in Detail

### Photo Management

- ✅ **Batch Upload** - Upload multiple photos with durable, tenant-carried processing + SSE progress
- ✅ **On-Demand Renders** - Gallery/masonry/retina served + cached from the single original via `[MediaRecipe]` (no stored derivatives)
- ✅ **EXIF Preservation** - Camera, lens, settings, GPS coordinates extracted (via ImageSharp) and stored
- ✅ **Event Organization** - Group photos into events (weddings, vacations, birthdays); auto daily-event on upload
- ✅ **Favorites & Ratings** - Mark favorites and rate photos 1-5 stars
- ✅ **Bulk Actions** - Select multiple photos for favorite, download, or delete operations

### AI-Powered Features

- ✅ **Vision Analysis** - AI analyzes composition, palette, lighting, mood, subjects
- ✅ **Structured Metadata** - Tags, summary, and facts returned as searchable JSON
- ✅ **Semantic Search** - Find photos by meaning: "sunset at beach", "people laughing"
- ✅ **Hybrid Search Mode** - User-controlled balance between keyword and semantic matching
- ✅ **Graceful Fallback** - Keyword search works even when AI services unavailable
- ✅ **Regenerate Analysis** - Re-analyze photos with updated prompts or models

### User Experience

- ✅ **Dark Theme Gallery** - Professional design with masonry grid layout
- ✅ **Lightbox Viewer** - Full-screen photo viewing with zoom, pan, navigation
- ✅ **Unified Info Panel** - EXIF metadata, AI insights, and actions in one place
- ✅ **Drag-and-Drop Upload** - Drag photos anywhere to start upload
- ✅ **Real-Time Progress** - Watch uploads and processing in floating monitor card
- ✅ **Keyboard Navigation** - Full keyboard control (arrows, ESC, shortcuts)
- ✅ **Accessibility** - ARIA labels, focus management, screen reader support
- ✅ **Responsive Design** - Works on desktop (mobile UI future enhancement)

---

## API Reference

The authoritative, file:line-cited surface is [snapvault-ui-api-contract.md](../../docs/architecture/snapvault-ui-api-contract.md). Highlights:

### Photos Endpoints

```http
GET    /api/photos                 # List (EntityController: filter/sort/pagination + X-Total-Count)
GET    /api/photos/{id}            # Get single photo
POST   /api/photos/upload          # Multipart upload → durable batch jobs (SSE progress)
POST   /api/photosets/query        # The windowed grid: all-photos/favorites/collection/search/event
GET    /api/photos/by-event/{eventId}
GET    /api/photos/{id}/download   # Full-resolution attachment
POST   /api/photos/{id}/favorite · /rate · /regenerate-ai · /regenerate-ai-analysis
POST   /api/photos/{id}/facts/{key}/toggle-lock · /summary/toggle-lock · /facts/lock-all · /unlock-all
POST   /api/photos/bulk/favorite · /bulk/delete
# Raw EntityController write verbs (Upsert/Patch/Delete/…) are SEALED to 405 — photos enter only via /upload,
# leave only via /bulk/delete. Semantic search is POST /api/photosets/query (context=search), not /photos/search.
```

### Events / Collections Endpoints

```http
GET    /api/events                 # List (EntityController, full array)
POST   /api/events                 # Create
GET    /api/collections            # List; + rename / add(capped) / remove
```

### Media, Progress & Studio↔Client

```http
GET    /media/{id}/{recipe}        # On-demand render (gallery/masonry/retina), access-scoped
GET    /api/photos/progress/{batchId}  # SSE upload progress (jobs-ledger projection)
POST   /api/gallery/invite · /api/gallery/accept        # studio invites → guest binds to identity
POST   /api/proofing/{photoId}                           # guest select/rate/comment (guest-write floor)
GET    /api/maintenance/stats · POST /api/maintenance/wipe-repository  # operator-only
```

---

## Configuration

### Configuration is Reference = Intent + `appsettings.json`

There is no imperative registration: referencing the connector projects (Mongo, Weaviate, Ollama, Storage.Local/S3)
in the `.csproj` is what enables them (`AddKoan()` discovers them). Providers/endpoints live in `appsettings.json`
(the `start.bat` orchestration wires the container endpoints). The `.NET` hierarchical config keys — overridable by
environment with the `__` separator — are:

```bash
Koan__Data__Mongo__Database=SnapVault
Koan__Storage__Providers__Local__BasePath=./storage    # dev
```

### Storage: one original, provider-swappable by config

`appsettings.json` binds the `cold` profile (where `PhotoAsset` originals live) to `Local` for dev; every render is
an on-demand `[MediaRecipe]` served from that single original — no per-tier derivative entities.

```jsonc
// appsettings.json (dev)                    // appsettings.Production.json (prod — same profile, S3 backend)
"Storage": {                                 "Storage": {
  "DefaultProfile": "cold",                    "Providers": { "S3": { "BucketPrefix": "snapvault",
  "Providers": { "Local": {                                          "Region": "us-east-1", "UseSsl": true } },
    "BasePath": "./storage" } },               "Profiles": { "cold": { "Provider": "s3", "Container": "photos" } }
  "Profiles": { "cold": {                    }
    "Provider": "local", "Container": "photos" } } }
```

**Production** swaps the *same* `cold` profile to `S3` with **zero entity or code change** (multi-provider
transparency). S3 credentials are supplied by the environment, never committed:
`Koan__Storage__Providers__S3__Endpoint`, `Koan__Storage__Providers__S3__AccessKey`, `…__SecretKey`.

---

## Learning Paths

### 🎓 New to Koan Framework?

**Start here**: Read the [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md) for a mentor-led tour of the codebase.

**Key sections**:
1. "Architecture Overview: The Big Picture" - Understand entity-first development
2. "Deep Dive: How Each Feature Works" - See patterns in action
3. "Common Patterns You'll Reuse" - Templates for your own apps

### 🔧 Want to Understand Specific Features?

**Image Processing**: `Services/PhotoProcessingService.cs` (lines 32-206)
**AI Vision**: `Services/PhotoProcessingService.cs` (lines 486-584)
**Semantic Search**: `Services/PhotoProcessingService.cs` (lines 249-297)
**Lightbox**: `wwwroot/js/components/lightbox.js`
**Upload Flow**: `wwwroot/js/components/upload.js`
**Real-Time Progress**: `Progress/UploadProgressProjection.cs` + `Controllers/UploadProgressController.cs` (SSE) + `wwwroot/js/components/processMonitor.js` (EventSource)

### 📚 Additional Documentation

- [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md) - Comprehensive developer guide (you are here's mentor)
- [docs/LIGHTBOX_OVERVIEW.md](./docs/LIGHTBOX_OVERVIEW.md) - Lightbox architecture and design
- [docs/LIGHTBOX_TECHNICAL_REFERENCE.md](./docs/LIGHTBOX_TECHNICAL_REFERENCE.md) - Implementation details
- [docs/UX-DESIGN-SPECIFICATION.md](./docs/UX-DESIGN-SPECIFICATION.md) - Design system and tokens

---

## Tech Stack

| Layer | Technology | Purpose |
|-------|-----------|---------|
| **Backend** | ASP.NET Core 8.0 | Web API framework |
| | Koan.Media | Image processing pipeline |
| | Koan.AI | Embeddings and vision |
| | Koan.Data | Multi-provider data access |
| | Koan.Data.Vector | Semantic search |
| | ImageSharp 3.x | EXIF extraction |
| | Koan.Jobs + Koan.Web.Sse | Durable processing + SSE progress |
| **Frontend** | Vanilla JavaScript (ES6 modules) | No build tools needed |
| | CSS Variables | Theme system |
| **Storage** | MongoDB | Photo metadata |
| | Weaviate | Vector search |
| | Ollama | Local AI (no API costs) |
| | Local File System | Development storage |
| **Production** | S3 / Azure Blob | Production storage tiers |
| | CloudFront / Azure CDN | CDN for hot tier |

---

## Roadmap / Future Enhancements

Potential additions to demonstrate more framework capabilities:

- [ ] **Face Recognition** - Tag photos with detected people
- [ ] **Duplicate Detection** - Find similar images with perceptual hashing
- [ ] **Albums/Collections** - User-created smart albums
- [ ] **Sharing** - Generate public links with expiry
- [ ] **Mobile App** - React Native / Flutter client
- [ ] **Basic Editing** - Crop, rotate, filters
- [ ] **Multi-User** - Authentication and user libraries
- [ ] **Storage Analytics** - Cost tracking and tier optimization

**Want to contribute?** Open an issue or PR to discuss new features!

---

## Troubleshooting

### Common Issues

**"AI analysis returns null"**
- Verify Ollama is running: `curl http://localhost:11434`
- Check orchestrator recommendations: `curl http://localhost:21434/v1/recommendations` (should list vision and embedding models)

**"Vector search not working"**
- Verify Weaviate is running: `curl http://localhost:8080/v1/.well-known/ready`
- Check browser console for errors during search

**"Upload stuck at processing"**
- Check the SSE stream in the browser Network tab (`/api/photos/progress/{batchId}`, an `EventSource`)
- Check server logs for `PhotoProcessingJob` handler exceptions (the durable job records failures on the ledger)

**"Images not displaying"**
- Verify `./.Koan/storage/` directory exists
- Check for `gallery/`, `thumbnails/` subdirectories
- Look for 404 errors in browser console

For detailed debugging, see [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md#troubleshooting-guide).

---

## Performance Characteristics

**Upload Processing**:
- Batch upload of 100 photos: ~2-3 minutes (includes derivatives + AI analysis)
- Single photo processing: ~1-2 seconds (derivatives only)
- AI vision analysis: ~5-30 seconds per photo (depends on model)

**Search Performance**:
- Semantic search (1000 photos): <500ms
- Keyword search (1000 photos): <100ms
- Hybrid search with alpha slider: <500ms

**Storage Efficiency**:
- 1 full-resolution photo (12MP, ~4MB): → 4MB cold + 200KB warm + 10KB hot = ~4.21MB total
- Three-tier approach reduces CDN bandwidth costs by >95%

---

## Credits and Acknowledgments

**Framework**: Koan Framework - Entity-first, multi-provider architecture
**AI Models**: Auto-selected by ZenGarden orchestrator advisor (vision, embedding, chat)
**Vector Database**: Weaviate
**Image Processing**: ImageSharp 3.x
**Design Inspiration**: Google Photos, Lightroom, Figma

---

## License

This sample is part of the Koan Framework repository and follows the same license.

---

## Get Help

- **Issues**: Open a GitHub issue for bugs or feature requests
- **Discussions**: Join framework discussions for questions
- **Documentation**: Start with [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md)

---

**Built with ❤️ to showcase what Koan Framework makes possible.**

🚀 Now go explore the code, run the app, and build something amazing!
