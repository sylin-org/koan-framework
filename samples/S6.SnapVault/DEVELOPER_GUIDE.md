# SnapVault Developer Guide

**A Comprehensive Tour of Building Production-Ready Photo Management with Koan Framework**

Welcome! This guide walks you through SnapVault, a complete, production-ready photo management application built to demonstrate Koan Framework's capabilities. Think of this as a conversation with a mentor who's already built the system and wants to share not just what was built, but why certain decisions were made and how everything fits together.

---

## What You're Looking At

SnapVault is a **self-hosted photo management system** that rivals commercial solutions like Google Photos or iCloud Photos. It's not a toy or proof-of-concept—it's a fully functional application you could actually use to manage your photo library.

### What Makes This Sample Special

Most framework samples show you how to build a todo list or basic CRUD app. SnapVault goes much further:

- **Real AI Integration**: Vision models analyze photos and generate structured metadata (tags, summaries, compositional analysis)
- **Semantic Search**: Natural language queries like "sunset at beach" or "people laughing" find relevant photos
- **Production UX**: Professional dark-themed gallery with keyboard shortcuts, accessibility support, and responsive design
- **Image Processing Pipeline**: Automatic generation of multiple derivatives (thumbnails, gallery views) with smart caching
- **Real-time Progress**: SignalR streams upload progress and processing status to the browser
- **Intelligent Storage**: Three-tier architecture (hot-cdn, warm, cold) optimizes costs and performance
- **Complete Feature Set**: Favorites, ratings, bulk operations, drag-drop upload, event organization, timeline views

This is what a **production Koan Framework application** looks like.

---

## Architecture Overview: The Big Picture

Let's start with the 30,000-foot view before diving into specifics.

### The Core Idea: Entity-First Development

Traditional apps structure code around repositories, services, and layers. Koan Framework flips this:

**Your entities ARE your data access layer.**

```csharp
// Traditional approach (what you DON'T do)
public class PhotoRepository : IPhotoRepository {
    private readonly DbContext _db;
    public async Task<Photo> GetAsync(string id) => await _db.Photos.FindAsync(id);
}

// Koan approach (what you DO)
public class PhotoAsset : MediaEntity<PhotoAsset> {
    // Entity defines its own structure
}

// Usage - no repository needed!
var photo = await PhotoAsset.Get(id);
await photo.Save();
```

**Why this matters**: You write less infrastructure code and focus on your domain model. The framework handles data access, storage, and relationships transparently.

### The Three-Tier Storage Strategy

SnapVault demonstrates intelligent storage tiering using `StorageBinding` attributes:

```csharp
// Full-resolution originals: Cold storage (S3 Glacier, Azure Archive)
[StorageBinding(Profile = "cold", Container = "photos")]
public class PhotoAsset : MediaEntity<PhotoAsset> { }

// Gallery views: Warm storage (S3 Standard, Azure Hot)
[StorageBinding(Profile = "warm", Container = "gallery")]
public class PhotoGallery : MediaEntity<PhotoGallery> { }

// Thumbnails: CDN-backed hot storage
[StorageBinding(Profile = "hot-cdn", Container = "thumbnails")]
public class PhotoThumbnail : MediaEntity<PhotoThumbnail> { }
```

**The pattern**: Different derivatives have different access patterns and cost profiles. Thumbnails are accessed constantly (hot), gallery views are accessed frequently (warm), originals are rarely accessed (cold).

### The AI Vision Pipeline

Recent additions to SnapVault showcase sophisticated AI integration:

1. **Vision Analysis**: Photos are analyzed by a vision model (qwen2.5vl via Ollama)
2. **Structured Output**: AI returns JSON with tags, summary, and detailed facts
3. **Embedding Generation**: Structured text is converted to vector embeddings
4. **Hybrid Search**: Queries use both semantic similarity (vectors) and keyword matching

**Why structured AI output?** Early iterations used comprehensive markdown prompts that caused hallucinations. The refined approach uses a ~70-line prompt that balances detail capture with accuracy.

---

## The Technology Stack

### Backend (C# / ASP.NET Core)

- **Koan.Media**: Image processing with fluent transformation API
- **Koan.AI**: Embeddings and vision analysis via Ollama
- **Koan.Data**: Multi-provider data access (MongoDB in this sample)
- **Koan.Data.Vector**: Semantic search via Weaviate
- **ImageSharp 3.x**: EXIF extraction and image manipulation
- **SignalR**: Real-time upload progress streaming

### Frontend (Vanilla JavaScript)

**Why vanilla JS?** This sample focuses on backend patterns. The frontend is kept simple intentionally—no build tools, no frameworks, just modern ES6 modules.

- **Component Architecture**: Each UI feature is a module (`grid.js`, `lightbox.js`, `upload.js`)
- **Design System**: CSS variables for theming, consistent spacing, typography
- **Accessibility**: ARIA labels, keyboard navigation, focus management
- **Progressive Enhancement**: Works without AI, degrades gracefully

### Infrastructure (Docker Compose)

- **MongoDB**: Photo metadata, events, relationships
- **Weaviate**: Vector similarity search
- **Ollama**: Local AI (no external API dependencies)

---

## Deep Dive: How Each Feature Works

### 1. Photo Upload Pipeline

**Entry Point**: `PhotosController.Upload()` (Controllers/PhotosController.cs:89)

The upload flow demonstrates several Koan patterns:

```csharp
[HttpPost("upload")]
public async Task<ActionResult<UploadResponse>> Upload(
    [FromForm] string? eventId,
    [FromForm] List<IFormFile> files,
    CancellationToken ct = default)
{
    // Create a processing job for progress tracking
    var job = new ProcessingJob {
        TotalPhotos = files.Count,
        Status = ProcessingStatus.InProgress
    };
    await job.Save(ct);

    // Queue each file for background processing
    foreach (var file in files) {
        _ = Task.Run(async () => {
            var photo = await _photoService.ProcessUploadAsync(eventId, file, job.Id, ct);
        });
    }

    return Ok(new UploadResponse { JobId = job.Id, TotalQueued = files.Count });
}
```

**Key Pattern**: Uploads return immediately with a job ID. Processing happens in background tasks that emit SignalR events.

**Why this approach?** Users shouldn't wait for image processing and AI analysis. Upload the file, show progress, let the user continue browsing.

### 2. Image Transformation Pipeline (DX-0047)

**Implementation**: `PhotoProcessingService.ProcessUploadAsync()` (Services/PhotoProcessingService.cs:32)

This showcases the **fluent media transformation API**:

```csharp
// Open source stream from upload
using var sourceStream = file.OpenReadStream();

// Upload full-resolution to cold storage
var fullResEntity = await PhotoAsset.Upload(sourceStream, file.FileName, file.ContentType, ct: ct);

// Create derivatives using fluent API
var galleryStream = await fullResEntity.OpenRead(ct);
var autoOriented = await galleryStream.AutoOrient(ct);
var resized = await autoOriented.ResizeFit(1200, 1200, ct);
var galleryResult = await resized.Result(ct);

// Branch 1: Gallery view (1200px max)
var galleryBranch = galleryResult.Branch();
var galleryEntity = await PhotoGallery.Upload(galleryBranch, $"{photo.Id}_gallery.jpg", "image/jpeg", ct: ct);

// Branch 2: Thumbnail (150x150 square crop)
var thumbnailBranch = galleryResult.Branch();
var cropped = await thumbnailBranch.CropSquare(ct: ct);
var thumbnailStream = await cropped.ResizeFit(150, 150, ct);
var thumbnailEntity = await PhotoThumbnail.Upload(thumbnailStream, $"{photo.Id}_thumb.jpg", "image/jpeg", ct: ct);

// Branch 3: Masonry thumbnail (300px max, preserve aspect ratio)
var masonryBranch = galleryResult.Branch();
var masonryResized = await masonryBranch.ResizeFit(300, 300, ct);
var masonryEntity = await PhotoMasonryThumbnail.Upload(masonryResized, $"{photo.Id}_masonry.jpg", "image/jpeg", ct: ct);
```

**Why branching?** The `Result()` call materializes the transformation pipeline into a stream. `Branch()` creates multiple read-only references to that stream without re-processing. This is **dramatically more efficient** than processing the image three separate times.

**Before DX-0047**: You'd write ~80 lines of operator loops with manual stream disposal. **After DX-0047**: 15 lines of declarative transformations.

### 3. AI Vision Analysis

**Implementation**: `PhotoProcessingService.GenerateDetailedDescriptionAsync()` (Services/PhotoProcessingService.cs:486)

The vision analysis pipeline evolved through several iterations:

**Iteration 1 (Comprehensive Prompt)**: ~1000-line markdown prompt with extensive examples → **Result**: Hallucinations
**Iteration 2 (Simplified JSON)**: ~50-line prompt with 4 core facts → **Result**: Better accuracy, but lacked detail
**Iteration 3 (Refined with Optional Details)**: ~70-line prompt with 7 core facts + optional categories → **Result**: Balanced detail and accuracy

Current approach:

```csharp
var prompt = @"Analyze this photograph and return a JSON object. Be accurate and only describe what you clearly see.

Return ONLY valid JSON in this exact format:

{
  ""tags"": [""tag1"", ""tag2"", ...],
  ""summary"": ""One clear sentence describing the image"",
  ""facts"": {
    ""Type"": ""portrait|landscape|still-life|..."",
    ""Subject Count"": ""describe number and type of subjects"",
    ""Composition"": ""describe framing and arrangement"",
    ""Palette"": ""list 3-5 dominant colors"",
    ""Lighting"": ""describe light source and quality"",
    ""Setting"": ""describe location context"",
    ""Mood"": ""describe emotional tone or atmosphere""
  }
}

IMPORTANT: Add additional fact fields for clearly visible details. Use descriptive field names.
Example: { ""Character"": ""female, dark hair, elf ears"", ""Atmospherics"": ""soft fog, god rays"" }

CRITICAL RULES:
1. Return ONLY the JSON object - no explanatory text
2. Only include what you can CLEARLY see - never guess or hallucinate
3. Use simple, factual descriptions
";
```

**Anti-Hallucination Strategy**:
- Emphasis on "only describe what you clearly see"
- Optional fields with guidance, not requirements
- Simple, factual descriptions over creative interpretation
- Multi-tier JSON parsing with fallbacks for markdown-wrapped responses

**Parsing Strategy** (`ParseAiResponse()` at line 426):

```csharp
// Strategy 1: Direct parse (model returned clean JSON)
// Strategy 2: Strip markdown code blocks and retry
// Strategy 3: Extract first JSON object using regex
```

**Why three strategies?** Vision models sometimes wrap JSON in markdown code blocks or add explanatory text. Robust parsing handles these variations gracefully.

### 4. Embedding Generation and Semantic Search

**Embedding Creation**: `PhotoProcessingService.GenerateAIMetadataAsync()` (Services/PhotoProcessingService.cs:209)

```csharp
// Build embedding text from structured analysis
var embeddingText = photo.AiAnalysis.ToEmbeddingText();
// Format: "tag1, tag2, tag3, summary sentence, fact1-value, fact2-value, ..."

// Generate embedding using Koan AI
var embedding = await Koan.AI.Ai.Embed(embeddingText, ct);

// Prepare vector metadata for hybrid search
var vectorMetadata = new Dictionary<string, object>
{
    ["originalFileName"] = photo.OriginalFileName,
    ["eventId"] = photo.EventId,
    ["searchText"] = embeddingText // Required for hybrid search
};

// Save with vector using framework pattern
await Data<PhotoAsset, string>.SaveWithVector(photo, embedding, vectorMetadata, ct);
```

**Search Execution**: `PhotoProcessingService.SemanticSearchAsync()` (Services/PhotoProcessingService.cs:249)

```csharp
// Generate query embedding
var queryVector = await Koan.AI.Ai.Embed(query, ct);

// Check if vector search is available
if (!Vector<PhotoAsset>.IsAvailable)
    return await FallbackKeywordSearch(query, eventId, topK, ct);

// Perform hybrid vector search with user-controlled alpha
var vectorResults = await Vector<PhotoAsset>.Search(
    vector: queryVector,
    text: query,      // Enables hybrid search
    alpha: alpha,     // User-controlled: 0.0 = exact, 1.0 = semantic
    topK: topK,
    ct: ct
);
```

**The Alpha Parameter**: Users can adjust the search mode:
- `alpha = 0.0`: Pure keyword matching (exact text search)
- `alpha = 0.5`: Balanced hybrid (50% semantic, 50% keyword)
- `alpha = 1.0`: Pure semantic (meaning-based search)

**Why hybrid?** Sometimes you want exact matches ("Canon 5D"), sometimes you want conceptual matches ("happy moments"). The slider in the UI lets users choose.

### 5. Frontend Architecture: Vanilla JS Components

The frontend is organized as ES6 modules with a component-based architecture:

**Core App** (`wwwroot/js/app.js`):
```javascript
class SnapVaultApp {
  constructor() {
    this.state = {
      photos: [],
      events: [],
      selectedPhotos: new Set(),
      filters: {},
      density: 4
    };
  }

  async init() {
    // Initialize components
    this.components.toast = new Toast();
    this.components.grid = new PhotoGrid(this);
    this.components.lightbox = new Lightbox(this);
    this.components.upload = new UploadModal(this);
    this.components.processMonitor = new ProcessMonitor(this);
    // ... more components

    // Setup event listeners
    this.setupDragAndDrop();
    this.setupWorkspaceNavigation();

    // Load initial data
    await this.loadPhotos();
    await this.loadEvents();
  }
}
```

**Key Components**:

- **`PhotoGrid`** (grid.js): Masonry layout with lazy loading, selection, quick actions
- **`Lightbox`** (lightbox.js): Full-screen photo viewer with zoom, navigation, metadata
- **`LightboxPanel`** (lightboxPanel.js): Unified info panel with EXIF, AI insights, actions
- **`LightboxZoom`** (lightboxZoom.js): Pan/zoom with mouse/trackpad/keyboard controls
- **`LightboxFocus`** (lightboxFocus.js): Accessibility focus management
- **`LightboxActions`** (lightboxActions.js): Favorite, rating, download, delete, AI regeneration
- **`UploadModal`** (upload.js): Drag-drop upload with event selection and progress
- **`ProcessMonitor`** (processMonitor.js): Floating progress card for background jobs
- **`SearchBar`** (search.js): Semantic search with alpha slider
- **`Filters`** (filters.js): Camera, rating, tag filters
- **`BulkActions`** (bulkActions.js): Multi-select operations
- **`KeyboardShortcuts`** (keyboard.js): Full keyboard navigation

**Component Communication Pattern**:
```javascript
// Components receive app instance in constructor
constructor(app) {
  this.app = app;
}

// Access shared state
this.app.state.photos

// Call other components
this.app.components.toast.show('Message');

// Update shared state
await this.app.loadPhotos(); // Refreshes grid
```

**Why this pattern?** Simple, explicit, easy to debug. No event bus complexity, no observer pattern overhead.

### 6. Lightbox: A Case Study in Progressive Enhancement

The lightbox demonstrates sophisticated UX patterns:

**Phase 1**: Basic image viewer with navigation
**Phase 2**: Zoom controls (click, keyboard, mouse wheel)
**Phase 3**: Unified info panel (EXIF + AI + Actions in one slide-out panel)
**Phase 4**: Pan/zoom with mouse drag and trackpad gestures
**Phase 5**: Keyboard navigation and shortcuts
**Phase 6**: Accessibility (ARIA labels, focus management, screen reader support)
**Phase 7**: AI description with regeneration capabilities

**Current State** (lightbox.js:1-550):

```javascript
class Lightbox {
  open(photoId) {
    // Load photo data
    this.currentPhoto = this.app.photos.find(p => p.id === photoId);

    // Display image
    this.render();

    // Open panel by default
    this.panel.open();

    // Setup zoom manager
    this.zoomManager.reset();

    // Setup keyboard navigation
    this.focusManager.captureFocus();
  }
}
```

**Panel Architecture** (lightboxPanel.js):

The panel is **unified** - one slide-out containing:
1. **EXIF Metadata**: Camera, lens, settings, date, GPS
2. **AI Insights**: Tags (chippable), summary, structured facts table
3. **Actions**: Favorite, rating, download, delete, regenerate AI

**Why unified?** Earlier versions had separate floating cards for EXIF and AI. Users found it cluttered. The unified panel follows modern design trends (like Figma's properties panel).

**AI Insights Rendering** (lightboxPanel.js:288-380):

```javascript
renderAIInsights(photo) {
  const analysis = photo.aiAnalysis;

  if (!analysis) {
    // Show "no analysis" state
    return;
  }

  // Render tags as chips
  const tagsHtml = analysis.tags.map(tag =>
    `<span class="tag-chip">${tag}</span>`
  ).join('');

  // Render summary
  const summaryHtml = `<p class="ai-summary">${analysis.summary}</p>`;

  // Render facts as table
  const factsHtml = Object.entries(analysis.facts).map(([key, value]) =>
    `<tr><td class="fact-label">${key}:</td><td class="fact-value">${value}</td></tr>`
  ).join('');

  // Combine into panel
  this.container.querySelector('.ai-content').innerHTML = `
    <div class="ai-tags">${tagsHtml}</div>
    ${summaryHtml}
    <table class="ai-facts">${factsHtml}</table>
  `;
}
```

**Regenerate AI** (lightboxActions.js:173-265):

Users can click "Regenerate" to re-analyze a photo:
1. Shows loading spinner
2. Calls `/api/photos/{id}/regenerate-ai`
3. Polls every 1s for updated analysis (max 60s timeout)
4. Updates panel when new analysis arrives
5. Shows toast notification

**Why polling?** The AI analysis can take 5-30 seconds depending on model speed. Polling keeps the UI responsive and handles failures gracefully.

### 7. Real-Time Progress with SignalR

**Backend Hub** (Hubs/PhotoProcessingHub.cs):

```csharp
public class PhotoProcessingHub : Hub
{
    public async Task JoinJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }

    public async Task LeaveJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }
}
```

**Emitting Progress** (PhotoProcessingService.cs:46-64):

```csharp
async Task EmitProgressAsync(string photoId, string status, string stage, string? error = null)
{
    await _hubContext.Clients.Group($"job:{jobId}").SendAsync("PhotoProgress", new PhotoProgressEvent
    {
        JobId = jobId,
        PhotoId = photoId,
        FileName = file.FileName,
        Status = status,  // "processing", "completed", "failed"
        Stage = stage,    // "upload", "exif", "thumbnails", "ai-description", "completed"
        Error = error
    });
}
```

**Frontend Listener** (processMonitor.js:98-140):

```javascript
async startJob(jobId, totalPhotos) {
  // Join SignalR group
  await this.connection.invoke('JoinJob', jobId);

  // Listen for progress events
  this.connection.on('PhotoProgress', (event) => {
    this.handlePhotoProgress(event);
  });

  // Listen for completion
  this.connection.on('JobCompleted', (event) => {
    this.handleJobCompleted(event);
    this.app.loadPhotos(); // Refresh gallery
  });
}
```

**UI States**:
- **Minimized**: Floating FAB with badge showing active uploads
- **Expanded**: Card showing per-photo progress with file names and stages
- **Completed**: Success toast, auto-minimize after 3s

### 8. Drag-and-Drop Upload

**Implementation** (app.js:363-412):

The drag-and-drop handler demonstrates careful event management:

```javascript
setupDragAndDrop() {
  const mainContent = document.querySelector('.main-content');
  let dragCounter = 0;

  // Prevent default drag behavior on entire page
  ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
    document.body.addEventListener(eventName, (e) => {
      e.preventDefault();
      e.stopPropagation();
    });
  });

  // Drag enter - show drop zone
  mainContent.addEventListener('dragenter', (e) => {
    dragCounter++;
    if (e.dataTransfer.types.includes('Files')) {
      mainContent.classList.add('drag-over');
    }
  });

  // Drag leave - hide drop zone
  mainContent.addEventListener('dragleave', () => {
    dragCounter--;
    if (dragCounter === 0) {
      mainContent.classList.remove('drag-over');
    }
  });

  // Drop - handle files
  mainContent.addEventListener('drop', (e) => {
    dragCounter = 0;
    mainContent.classList.remove('drag-over');

    const files = e.dataTransfer.files;
    if (files.length > 0) {
      // Open upload modal with pre-selected files
      this.components.upload.open(files);
    }
  });
}
```

**The Drag Counter Trick**: Without a counter, `dragleave` fires when hovering over child elements, causing flickering. The counter tracks nested drag events correctly.

**Visual Feedback** (app.css:522-564):

```css
.main-content.drag-over::before {
  content: '';
  /* Blue overlay with dashed border */
}

.main-content.drag-over::after {
  content: '📤 Drop photos here to upload';
  /* Centered message with backdrop blur */
}
```

---

## Data Model Deep Dive

### Entity Relationships

```
Event (1) ──────────── (N) PhotoAsset
                           │
                           ├─ (1) PhotoGallery
                           ├─ (1) PhotoThumbnail
                           └─ (1) PhotoMasonryThumbnail

ProcessingJob (1) ──── (N) [Background Tasks]
```

### Key Entities

**PhotoAsset** (Models/PhotoAsset.cs):
- **Purpose**: Represents full-resolution photo in cold storage
- **Properties**: EXIF metadata, AI analysis, event relationship, processing status
- **Storage**: `[StorageBinding(Profile = "cold", Container = "photos")]`

**PhotoGallery** (Models/PhotoGallery.cs):
- **Purpose**: 1200px max dimension for lightbox viewing
- **Storage**: `[StorageBinding(Profile = "warm", Container = "gallery")]`

**PhotoThumbnail** (Models/PhotoThumbnail.cs):
- **Purpose**: 150x150 square crop for grid views
- **Storage**: `[StorageBinding(Profile = "hot-cdn", Container = "thumbnails")]`

**PhotoMasonryThumbnail** (Models/PhotoMasonryThumbnail.cs):
- **Purpose**: 300px max (preserves aspect ratio) for masonry grid
- **Storage**: `[StorageBinding(Profile = "hot-cdn", Container = "masonry-thumbnails")]`

**Event** (Models/Event.cs):
- **Purpose**: Organizational unit for photos (wedding, vacation, etc.)
- **Properties**: Name, type, date, photo count, storage tier

**ProcessingJob** (Models/ProcessingJob.cs):
- **Purpose**: Tracks batch upload progress
- **Properties**: Total photos, processed count, errors, status

**AiAnalysis** (Models/AiAnalysis.cs):
- **Purpose**: Structured AI vision output
- **Properties**: Tags (List<string>), Summary (string), Facts (Dictionary<string, string>)
- **Methods**: `ToEmbeddingText()` for vector generation

### Storage Profiles

Configured in `Initialization/KoanAutoRegistrar.cs`:

```csharp
services.AddKoan(config => {
    config.UseLocalFileStorage(opts => {
        opts.BasePath = "./.Koan/storage";

        opts.AddProfile("hot-cdn", profile => {
            profile.Container = "thumbnails";
            profile.UseCache = true;
            profile.CacheMaxAge = TimeSpan.FromDays(30);
        });

        opts.AddProfile("warm", profile => {
            profile.Container = "gallery";
            profile.UseCache = true;
            profile.CacheMaxAge = TimeSpan.FromDays(7);
        });

        opts.AddProfile("cold", profile => {
            profile.Container = "photos";
            profile.UseCache = false;
        });
    });
});
```

**Production Mapping**:
- `hot-cdn` → CloudFront + S3 / Azure CDN + Blob Storage
- `warm` → S3 Standard / Azure Hot Storage
- `cold` → S3 Glacier / Azure Archive Storage

---

## API Design Patterns

### RESTful Endpoints

**Koan's EntityController Pattern**:

```csharp
[Route("api/[controller]")]
public class PhotosController : EntityController<PhotoAsset>
{
    // Inherits standard CRUD:
    // GET /api/photos - Query all
    // GET /api/photos/{id} - Get by ID
    // POST /api/photos - Create
    // PUT /api/photos/{id} - Update
    // DELETE /api/photos/{id} - Delete

    // Custom endpoints:
    [HttpPost("upload")]
    public async Task<ActionResult<UploadResponse>> Upload(...) { }

    [HttpPost("search")]
    public async Task<ActionResult<SearchResponse>> Search(...) { }
}
```

**Why inherit EntityController?** You get standard CRUD for free. Add custom endpoints only when you need business logic beyond basic data access.

### Custom Endpoints

**Upload with Background Processing**:
```csharp
[HttpPost("upload")]
public async Task<ActionResult<UploadResponse>> Upload(
    [FromForm] string? eventId,
    [FromForm] List<IFormFile> files,
    CancellationToken ct = default)
```

**Semantic Search**:
```csharp
[HttpPost("search")]
public async Task<ActionResult<SearchResponse>> Search(
    [FromBody] SearchRequest request,
    CancellationToken ct = default)
```

**Photo Actions**:
```csharp
[HttpPost("{id}/favorite")]
public async Task<ActionResult<FavoriteResponse>> ToggleFavorite(string id, ...)

[HttpPost("{id}/rate")]
public async Task<ActionResult<RateResponse>> RatePhoto(string id, [FromBody] RateRequest request, ...)

[HttpPost("{id}/regenerate-ai")]
public async Task<IActionResult> RegenerateAI(string id, ...)
```

**Bulk Operations**:
```csharp
[HttpPost("bulk/delete")]
public async Task<ActionResult<BulkDeleteResponse>> BulkDelete([FromBody] BulkDeleteRequest request, ...)
```

---

## Configuration and Deployment

### Development Setup

**Using start.bat** (Windows) or **start.sh** (macOS/Linux):

```bash
# Automatically:
# 1. Checks for Docker
# 2. Builds container image
# 3. Starts dependencies (MongoDB, Weaviate, Ollama)
# 4. Runs the application
# 5. Opens browser to http://localhost:5086
./start.bat
```

### Docker Compose Configuration

**File**: `docker-compose.yml`

```yaml
services:
  mongodb:
    image: mongo:latest
    ports: ["27017:27017"]
    volumes: ["mongodb-data:/data/db"]

  weaviate:
    image: semitechnologies/weaviate:latest
    ports: ["8080:8080"]
    environment:
      PERSISTENCE_DATA_PATH: "/var/lib/weaviate"

  ollama:
    image: ollama/ollama:latest
    ports: ["11434:11434"]
    volumes: ["ollama-data:/root/.ollama"]
```

### Environment Variables

**File**: `.env` (not committed, created by start.bat)

```bash
# Data Provider
KOAN_DATA_PROVIDER=mongodb
KOAN_MONGODB_CONNECTION=mongodb://localhost:27017

# Vector Provider
KOAN_VECTOR_PROVIDER=weaviate
KOAN_WEAVIATE_ENDPOINT=http://localhost:8080

# AI Provider
KOAN_AI_PROVIDER=ollama
KOAN_OLLAMA_ENDPOINT=http://localhost:11434

# Storage
KOAN_STORAGE_PROVIDER=localfile
KOAN_STORAGE_BASEPATH=./.Koan/storage
```

### Production Considerations

**Storage Backends**:
- Replace LocalFileStorage with S3Storage or AzureBlobStorage
- Configure CDN for hot-cdn profile
- Use Glacier/Archive for cold profile

**Database Scaling**:
- MongoDB replica sets for high availability
- Weaviate cluster for vector search at scale

**AI Processing**:
- Consider cloud AI services (OpenAI, Azure Computer Vision) for production
- Ollama is great for development/self-hosting, but cloud services offer better performance

**Caching**:
- Add Redis for session storage and output caching
- Configure HTTP caching headers for derivatives

---

## Testing Strategy

### What to Test

**Backend**:
1. **Image Processing Pipeline**: Verify derivatives are generated correctly
2. **EXIF Extraction**: Ensure metadata is captured accurately
3. **AI Integration**: Mock vision model responses, test parsing
4. **Search**: Test both vector and keyword search paths
5. **Error Handling**: Network failures, invalid files, missing services

**Frontend**:
1. **Upload Flow**: File selection, validation, progress tracking
2. **Lightbox Navigation**: Keyboard, mouse, touch gestures
3. **Search**: Query parsing, mode switching, result rendering
4. **Accessibility**: Screen reader navigation, keyboard-only usage

### Testing Tools

**Backend** (xUnit + FluentAssertions):
```csharp
[Fact]
public async Task ProcessUpload_CreatesAllDerivatives()
{
    // Arrange
    var service = new PhotoProcessingService(...);
    var file = CreateTestImage(1920, 1080);

    // Act
    var photo = await service.ProcessUploadAsync(null, file, "job123", ct);

    // Assert
    photo.Should().NotBeNull();
    photo.GalleryMediaId.Should().NotBeNullOrEmpty();
    photo.ThumbnailMediaId.Should().NotBeNullOrEmpty();

    var gallery = await PhotoGallery.Get(photo.GalleryMediaId);
    gallery.Width.Should().BeLessOrEqualTo(1200);
}
```

**Frontend** (Manual + Browser DevTools):
- Use browser accessibility tools to verify ARIA labels
- Test keyboard navigation with screen reader
- Verify responsive behavior at different viewport sizes

---

## Performance Optimization Strategies

### Image Processing

**Branching is Critical**:
```csharp
// ❌ Bad: Process the image three times
var gallery = await photo.OpenRead().AutoOrient().ResizeFit(1200, 1200);
var thumb = await photo.OpenRead().AutoOrient().CropSquare().ResizeFit(150, 150);
var masonry = await photo.OpenRead().AutoOrient().ResizeFit(300, 300);

// ✅ Good: Process once, branch three times
var result = await photo.OpenRead().AutoOrient().Result();
var gallery = await result.Branch().ResizeFit(1200, 1200);
var thumb = await result.Branch().CropSquare().ResizeFit(150, 150);
var masonry = await result.Branch().ResizeFit(300, 300);
```

**Async Processing**:
- Upload returns immediately with job ID
- Image processing happens in background
- AI analysis runs after upload completes
- SignalR streams progress to browser

### Frontend Rendering

**Virtual Scrolling** (Not Yet Implemented):
- Grid currently renders all photos
- For libraries > 1000 photos, consider virtual scrolling
- Intersection Observer API for lazy-loading images

**Debouncing Search**:
```javascript
// searchBar.js uses debouncing for real-time search
let searchTimeout;
searchInput.addEventListener('input', (e) => {
  clearTimeout(searchTimeout);
  searchTimeout = setTimeout(() => {
    this.performSearch(e.target.value);
  }, 300);
});
```

### Database Queries

**Indexed Fields**:
- PhotoAsset.EventId (event filtering)
- PhotoAsset.CapturedAt (timeline sorting)
- Event.EventDate (timeline grouping)

**Pagination**:
```csharp
[HttpGet("by-event/{eventId}")]
public async Task<ActionResult<PhotoListResponse>> GetPhotosByEvent(
    string eventId,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    CancellationToken ct = default)
{
    var photos = await PhotoAsset.Query(p => p.EventId == eventId, ct);
    var paginated = photos.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    return Ok(new PhotoListResponse {
        Photos = paginated,
        Page = page,
        PageSize = pageSize,
        TotalCount = photos.Count
    });
}
```

---

## Common Patterns You'll Reuse

### 1. Multi-Derivative Media Processing

**Pattern**:
1. Upload source to cold storage
2. Materialize transformation pipeline with `Result()`
3. Branch for each derivative
4. Store derivatives with appropriate storage profiles

**Use Cases**:
- Video transcoding (multiple resolutions)
- Audio processing (different bitrates)
- Document generation (PDF, thumbnails, previews)

### 2. Background Processing with Progress

**Pattern**:
1. Create job entity with total count
2. Return job ID immediately
3. Process items in background
4. Emit SignalR events for progress
5. Update job entity on completion

**Use Cases**:
- Batch imports
- Report generation
- Data migrations
- Export operations

### 3. Hybrid Search

**Pattern**:
1. Generate embeddings for entities
2. Store with `SaveWithVector()`
3. Let users control alpha (semantic vs keyword)
4. Provide fallback for when vector search unavailable

**Use Cases**:
- Document search
- Product discovery
- Content recommendations
- Knowledge base queries

### 4. Structured AI Output

**Pattern**:
1. Design clear JSON schema for AI response
2. Emphasize "only describe what you see" in prompt
3. Implement multi-tier parsing (direct, markdown-stripped, regex extraction)
4. Validate and provide error states

**Use Cases**:
- Image captioning
- Document classification
- Content moderation
- Data extraction from unstructured sources

---

## Troubleshooting Guide

### Common Issues

**"AI analysis returns null"**
- Check Ollama is running: `curl http://localhost:11434`
- Verify model is pulled: `ollama list` (should show `all-minilm` and `qwen2.5vl`)
- Check logs for JSON parsing failures

**"Vector search not working"**
- Verify Weaviate is running: `curl http://localhost:8080/v1/.well-known/ready`
- Check `Vector<PhotoAsset>.IsAvailable` returns true
- Ensure embeddings were generated (check `photo.Embedding` is not null)

**"Images not displaying"**
- Check storage path exists: `./.Koan/storage/`
- Verify derivatives were created: Look for `gallery/`, `thumbnails/` subdirectories
- Check browser console for 404 errors on media URLs

**"Upload gets stuck at processing"**
- Check SignalR connection: Look for `[SignalR] Connected` in browser console
- Verify PhotoProcessingHub is registered in Program.cs
- Check background task exceptions in server logs

### Debugging Tips

**Backend**:
```csharp
// Enable detailed logging
builder.Services.AddLogging(logging => {
    logging.SetMinimumLevel(LogLevel.Debug);
});

// Log transformation pipeline
_logger.LogDebug("Starting transformation for {PhotoId}", photo.Id);
var result = await gallery.OpenRead().AutoOrient().ResizeFit(1200, 1200);
_logger.LogDebug("Transformation complete for {PhotoId}", photo.Id);
```

**Frontend**:
```javascript
// Enable verbose logging in components
console.log('[Lightbox] Opening photo:', photoId);
console.log('[Search] Query:', query, 'Alpha:', alpha);
console.log('[Upload] Files selected:', files.length);
```

---

## Where to Find Specific Implementations

### Backend (C#)

**Controllers** (business logic entry points):
- `Controllers/PhotosController.cs` - Photo upload, search, actions (lines 1-450)
- `Controllers/EventsController.cs` - Event management, timeline (lines 1-250)

**Services** (core processing):
- `Services/PhotoProcessingService.cs` - Image transformations, EXIF, AI analysis (lines 1-600)

**Models** (entities and contracts):
- `Models/PhotoAsset.cs` - Main photo entity (lines 1-80)
- `Models/AiAnalysis.cs` - Structured AI output (lines 1-90)
- `Models/Event.cs` - Event organization (lines 1-60)

**Initialization** (framework configuration):
- `Initialization/KoanAutoRegistrar.cs` - Auto-registration (lines 1-150)

**Hubs** (real-time communication):
- `Hubs/PhotoProcessingHub.cs` - SignalR progress streaming (lines 1-30)

### Frontend (JavaScript)

**Core App**:
- `wwwroot/js/app.js` - Main application class (lines 1-420)

**Components**:
- `wwwroot/js/components/grid.js` - Photo grid with masonry (lines 1-350)
- `wwwroot/js/components/lightbox.js` - Lightbox viewer (lines 1-550)
- `wwwroot/js/components/lightboxPanel.js` - Info panel (lines 1-530)
- `wwwroot/js/components/lightboxZoom.js` - Zoom/pan controls (lines 1-250)
- `wwwroot/js/components/lightboxActions.js` - Photo actions (lines 1-270)
- `wwwroot/js/components/upload.js` - Upload modal (lines 1-400)
- `wwwroot/js/components/processMonitor.js` - Progress tracking (lines 1-300)
- `wwwroot/js/components/search.js` - Search bar (lines 1-200)
- `wwwroot/js/components/keyboard.js` - Shortcuts (lines 1-150)

**Styles**:
- `wwwroot/css/app.css` - Main application styles (lines 1-2500)
- `wwwroot/css/lightbox.css` - Lightbox viewer styles (lines 1-800)
- `wwwroot/css/lightbox-panel.css` - Panel styles (lines 1-600)

**API Client**:
- `wwwroot/js/api.js` - Fetch wrapper (lines 1-100)

### Documentation

**Current Documentation**:
- `README.md` - Sample overview and quickstart
- `DEVELOPER_GUIDE.md` - This comprehensive guide
- `docs/LIGHTBOX_OVERVIEW.md` - Lightbox architecture
- `docs/LIGHTBOX_TECHNICAL_REFERENCE.md` - Lightbox implementation details
- `docs/UX-DESIGN-SPECIFICATION.md` - Design system
- `docs/SETTINGS-PAGE-DESIGN.md` - Settings page design (future)

**Archived Documentation** (development history):
- `docs/archive/LIGHTBOX_PHASE_*.md` - Phase-by-phase implementation
- `docs/archive/LIGHTBOX_TESTING.md` - Testing strategy
- `docs/archive/LIGHTBOX_REDESIGN.md` - Original redesign proposal
- `docs/archive/DELTA-ANALYSIS.md` - Diff analysis
- `docs/archive/CONFIGURATION-CLEANUP.md` - Config refactoring

---

## Next Steps: Extending SnapVault

### Feature Ideas

**1. Face Recognition**
- Use face detection models to identify people
- Tag photos with recognized faces
- Search "photos with John"

**2. Duplicate Detection**
- Perceptual hashing to find similar images
- Side-by-side comparison UI
- Bulk merge/delete duplicates

**3. Albums/Collections**
- User-created collections beyond events
- Drag-drop photos to albums
- Smart albums based on filters

**4. Sharing**
- Generate shareable links with expiry
- Public galleries with password protection
- Social sharing (Facebook, Twitter)

**5. Mobile App**
- React Native or Flutter client
- Camera upload integration
- Offline viewing

**6. Advanced Editing**
- Basic adjustments (crop, rotate, brightness)
- Filters and presets
- Non-destructive editing

### Learning Paths

**If you want to learn more about...**

**Image Processing**: Study `PhotoProcessingService.cs` and DX-0047 fluent API patterns

**AI Integration**: Review AI vision prompts, parsing strategies, and embedding generation

**Real-time Features**: Explore SignalR integration between `PhotoProcessingHub` and `ProcessMonitor`

**Frontend Architecture**: Analyze component communication patterns in `app.js` and component modules

**Storage Tiering**: Examine `StorageBinding` attributes and profile configurations

**Entity Relationships**: Study entity models and relationship navigation patterns

---

## Closing Thoughts

SnapVault demonstrates that **Koan Framework enables building sophisticated applications with dramatically less code than traditional approaches**.

Compare:
- **No repository classes** - Entities are self-sufficient
- **No service layers** for basic CRUD - EntityController provides it
- **No manual configuration** - Auto-registration discovers and wires everything
- **No storage adapters** - MediaEntity + StorageBinding handles it
- **No ORM ceremony** - Entity<T> just works

What you **do write**:
- Domain models that express your business logic
- Transformation pipelines that describe intent
- AI prompts that capture requirements
- UI components that delight users

**That's the Koan Framework philosophy**: Let the framework handle infrastructure so you can focus on what makes your application unique.

Now go build something amazing! 🚀
