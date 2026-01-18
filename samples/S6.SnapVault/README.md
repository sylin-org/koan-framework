# SnapVault Pro - Complete Photo Management System

**A production-ready photo management application showcasing Koan Framework's full capabilities: media processing, AI vision, semantic search, and modern web UX.**

![Status](https://img.shields.io/badge/status-production--ready-success)
![Framework](https://img.shields.io/badge/koan-v0.6.3-blue)
![Backend](https://img.shields.io/badge/backend-ASP.NET_Core-purple)
![Frontend](https://img.shields.io/badge/frontend-vanilla_js-yellow)

---

## ✨ NEW: AI-0020 Modernization (2025-01)

SnapVault has been **fully modernized** with the latest AI-0020 patterns:

🔒 **Transaction Coordination** - Atomic commits prevent orphaned vectors (MongoDB + Weaviate)
🏷️ **[Embedding] Attribute** - Declarative embedding generation with lifecycle hooks
🚀 **Pipeline API** - Fluent vision analysis: `Ai.FromImage().ToText()`
📊 **Production Monitoring** - Real-time cost tracking, success rate, latency alerts
📉 **62.5% Code Reduction** - ~250 lines eliminated (400 → 150 AI-related code)

**See**: [MIGRATION_GUIDE.md](./MIGRATION_GUIDE.md) for complete before/after comparison.

---

## What is SnapVault?

SnapVault is a **self-hosted photo management system** built with Koan Framework that rivals commercial solutions like Google Photos. It's not a toy example—it's a complete, production-ready application demonstrating:

✨ **AI Vision Analysis** - Vision models analyze photos and generate structured metadata (tags, summaries, compositional details)
🔍 **Semantic Search** - Natural language queries like "sunset at beach" or "people laughing"
🎨 **Modern Dark UI** - Professional gallery with keyboard shortcuts, accessibility, and responsive design
⚡ **Smart Processing** - Automatic generation of optimized derivatives (thumbnails, gallery views) with intelligent caching
📊 **Real-time Progress** - SignalR streams upload progress and processing status
💾 **Intelligent Storage** - Three-tier architecture (hot-cdn, warm, cold) optimizes costs and performance
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

### 🖼️ Media Processing (DX-0047 Fluent API)

Process images using declarative transformation pipelines with automatic resource management:

```csharp
// Create multiple derivatives efficiently with branching
var result = await photo.OpenRead().AutoOrient().ResizeFit(1200, 1200).Result();

var gallery = await result.Branch().Upload<PhotoGallery>("gallery.jpg");
var thumb = await result.Branch().CropSquare().ResizeFit(150, 150).Upload<PhotoThumbnail>();
var masonry = await result.Branch().ResizeFit(300, 300).Upload<PhotoMasonryThumbnail>();
```

**Why it matters**: Before DX-0047, this required ~80 lines of operator loops. The fluent API reduces it to 15 lines.

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

// 2. Vision analysis with fluent pipeline API
var analysisJson = await Ai.FromImage(imageBytes, "image/jpeg")
    .ToText(structuredJsonPrompt, model: "qwen2.5vl");

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

### 💾 Multi-Tier Storage Architecture

Optimize storage costs with declarative tier assignment:

```csharp
// Full-resolution originals in cold storage
[StorageBinding(Profile = "cold", Container = "photos")]
public class PhotoAsset : MediaEntity<PhotoAsset> { }

// Gallery views in warm storage
[StorageBinding(Profile = "warm", Container = "gallery")]
public class PhotoGallery : MediaEntity<PhotoGallery> { }

// Thumbnails in CDN-backed hot storage
[StorageBinding(Profile = "hot-cdn", Container = "thumbnails")]
public class PhotoThumbnail : MediaEntity<PhotoThumbnail> { }
```

**Production mapping**: hot-cdn → CloudFront, warm → S3 Standard, cold → Glacier

### 📡 Real-Time Progress Tracking

Stream processing status to the browser with SignalR:

```csharp
// Backend: Emit progress events
await _hubContext.Clients.Group($"job:{jobId}").SendAsync("PhotoProgress", new {
    PhotoId = photo.Id,
    Stage = "ai-description",  // upload → exif → thumbnails → ai-description → completed
    Status = "processing"
});

// Frontend: Listen and update UI
connection.on('PhotoProgress', (event) => {
    updateProgressCard(event.photoId, event.stage);
});
```

**Why it matters**: Background processing with real-time feedback creates professional UX.

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

```csharp
// 1. Create job for tracking
var job = new ProcessingJob { TotalPhotos = files.Count };
await job.Save();

// 2. Return immediately with job ID
return Ok(new { JobId = job.Id });

// 3. Process in background
_ = Task.Run(() => ProcessPhotos(files, job.Id));

// 4. Emit SignalR events for progress
await _hub.Clients.Group($"job:{job.Id}").SendAsync("PhotoProgress", ...);
```

---

## Project Structure

```
samples/S6.SnapVault/
├── Controllers/
│   ├── PhotosController.cs      # Photo upload, search, actions
│   └── EventsController.cs      # Event management, timeline
├── Services/
│   └── PhotoProcessingService.cs # Image transformations, EXIF, AI
├── Models/
│   ├── PhotoAsset.cs            # Main photo entity (cold storage)
│   ├── PhotoGallery.cs          # Gallery derivative (warm)
│   ├── PhotoThumbnail.cs        # Thumbnail (hot-cdn)
│   ├── Event.cs                 # Event organization
│   └── AiAnalysis.cs            # Structured AI output
├── Hubs/
│   └── PhotoProcessingHub.cs    # SignalR progress streaming
├── Initialization/
│   └── KoanAutoRegistrar.cs     # Framework configuration
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

- ✅ **Batch Upload** - Upload multiple photos with progress tracking
- ✅ **Automatic Derivatives** - Thumbnails, gallery views, masonry variants generated automatically
- ✅ **EXIF Preservation** - Camera, lens, settings, GPS coordinates extracted and stored
- ✅ **Event Organization** - Group photos into events (weddings, vacations, birthdays)
- ✅ **Timeline View** - Browse photos chronologically with month/year grouping
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

### Photos Endpoints

```http
GET    /api/photos              # Query all photos
GET    /api/photos/{id}         # Get single photo
POST   /api/photos/upload       # Upload with batch processing
POST   /api/photos/search       # Semantic + keyword search
GET    /api/photos/by-event/{id} # Get photos for event
POST   /api/photos/{id}/favorite # Toggle favorite
POST   /api/photos/{id}/rate    # Set rating (0-5)
GET    /api/photos/{id}/download # Download full-resolution
POST   /api/photos/{id}/regenerate-ai  # Re-analyze photo
POST   /api/photos/bulk/delete  # Delete multiple photos
```

### Events Endpoints

```http
GET    /api/events              # Query all events
GET    /api/events/{id}         # Get single event
POST   /api/events              # Create new event
DELETE /api/events/{id}         # Delete event
GET    /api/events/timeline     # Get timeline grouped by month/year
```

---

## Configuration

### Environment Variables

The start script creates a `.env` file automatically. For manual configuration:

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

### Storage Profiles

Configured in `Initialization/KoanAutoRegistrar.cs`:

```csharp
config.UseLocalFileStorage(opts => {
    opts.AddProfile("hot-cdn", profile => {
        profile.UseCache = true;
        profile.CacheMaxAge = TimeSpan.FromDays(30);
    });

    opts.AddProfile("warm", profile => {
        profile.UseCache = true;
        profile.CacheMaxAge = TimeSpan.FromDays(7);
    });

    opts.AddProfile("cold", profile => {
        profile.UseCache = false;
    });
});
```

**Production**: Replace with `UseS3Storage()` or `UseAzureBlobStorage()` and map profiles to appropriate tiers.

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
**Real-Time Progress**: `Hubs/PhotoProcessingHub.cs` + `wwwroot/js/components/processMonitor.js`

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
| | SignalR | Real-time progress |
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
- Check models are pulled: `ollama list` (should show `all-minilm`, `qwen2.5vl`)

**"Vector search not working"**
- Verify Weaviate is running: `curl http://localhost:8080/v1/.well-known/ready`
- Check browser console for errors during search

**"Upload stuck at processing"**
- Check SignalR connection in browser console
- Verify PhotoProcessingHub is registered in Program.cs
- Check server logs for background task exceptions

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
**Vision Model**: Qwen2.5-VL (Alibaba Cloud) via Ollama
**Embedding Model**: all-minilm (sentence-transformers) via Ollama
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
