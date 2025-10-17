# SnapVault

**A modern photo management system demonstrating media processing, AI-powered search, and tiered storage with Koan Framework.**

SnapVault shows you how to build a complete photo management application with automatic derivative generation (thumbnails, gallery views), semantic search powered by vector embeddings, EXIF metadata extraction, and intelligent storage tiering. This sample walks through building event-based photo organization, real-time image processing, and a responsive web interface using Koan's media processing capabilities.

## What this sample teaches

**Media processing with DX-0047 Fluent API**
This sample demonstrates the new fluent media transformation pipeline for server-side image processing. You'll learn to create multiple image derivatives efficiently using method chaining, branching for parallel processing, and automatic stream disposal.

**Multi-tier storage architecture**
See how to implement intelligent storage tiers (hot-cdn for thumbnails, warm for gallery views, cold for originals) using Koan's StorageEntity pattern with declarative `[StorageBinding]` attributes.

**AI-powered semantic search**
Learn to integrate vector embeddings for semantic photo search ("sunset at beach", "family gathering") while maintaining graceful fallbacks when AI services are unavailable.

**EXIF metadata extraction**
The sample shows how to extract and preserve camera metadata (GPS coordinates, camera settings, capture time) using ImageSharp's ExifProfile API.

**Production-ready patterns**
Implements real-world concerns: batch photo uploads with progress tracking, background AI processing, multi-entity relationships, SignalR for real-time updates, and error handling throughout the pipeline.

## How to build an app like this

**[1] Model your media entities with storage tiers**
Define entities for different image sizes using `MediaEntity<T>` with `StorageBinding` attributes to specify storage profiles.

**[2] Implement fluent transformation pipelines**
Use DX-0047 fluent API to create image derivatives with automatic orientation, resizing, cropping, and format conversion in declarative pipelines.

**[3] Extract and preserve metadata**
Build EXIF extraction to capture camera information, GPS coordinates, and shooting parameters that enhance photo organization.

**[4] Add semantic search with vectors**
Generate embeddings from photo metadata and content descriptions to enable AI-powered search alongside traditional filters.

**[5] Organize with event-based structure**
Create an event hierarchy for photo organization (weddings, vacations, birthdays) with timeline views and tier-based archival.

**[6] Build responsive photo galleries**
Implement grid layouts with lazy loading, lightbox views, and real-time upload progress using SignalR.

## Building the photo management system step-by-step

### Step 1: Define multi-tier media entities

Model photos with separate entities for storage optimization:

```csharp
// Full-resolution photo in cold storage
[StorageBinding(Profile = "cold", Container = "photos")]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string EventId { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; }

    // EXIF metadata
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public int? ISO { get; set; }
    public string? FocalLength { get; set; }
    public GpsCoordinates? Location { get; set; }

    // AI-generated metadata
    public List<string> DetectedObjects { get; set; } = new();
    public string MoodDescription { get; set; } = "";
    public List<string> AutoTags { get; set; } = new();

    // Vector embedding for semantic search
    public float[]? Embedding { get; set; }

    // Derivative relationships
    public string? GalleryMediaId { get; set; }

    public ProcessingStatus ProcessingStatus { get; set; }
}

// Gallery-size derivative in warm storage (1200px max)
[StorageBinding(Profile = "warm", Container = "gallery")]
public class PhotoGallery : MediaEntity<PhotoGallery>
{
    public int Width { get; set; }
    public int Height { get; set; }
}

// Thumbnail in hot-cdn storage (150x150)
[StorageBinding(Profile = "hot-cdn", Container = "thumbnails")]
public class PhotoThumbnail : MediaEntity<PhotoThumbnail>
{
    public int Width { get; set; }
    public int Height { get; set; }
}
```

This three-tier approach optimizes storage costs and access patterns.

### Step 2: Implement fluent media transformation

Process uploads using DX-0047 fluent API with branching for derivatives:

```csharp
public async Task<PhotoAsset> ProcessUploadAsync(string eventId, IFormFile file, CancellationToken ct)
{
    var photo = new PhotoAsset
    {
        EventId = eventId,
        OriginalFileName = file.FileName,
        ProcessingStatus = ProcessingStatus.InProgress
    };

    using var sourceStream = file.OpenReadStream();

    // Extract EXIF before transformations
    await ExtractExifMetadataAsync(photo, sourceStream, ct);
    sourceStream.Position = 0;

    // Upload full-resolution to cold storage
    var fullResEntity = await PhotoAsset.Upload(sourceStream, file.FileName, file.ContentType, ct: ct);
    photo.Id = fullResEntity.Id;
    photo.Key = fullResEntity.Key;

    // Use DX-0047 fluent API to create derivatives
    var galleryStream = await fullResEntity.OpenRead(ct);
    var autoOriented = await galleryStream.AutoOrient(ct);
    var resized = await autoOriented.ResizeFit(1200, 1200, ct);
    var galleryResult = await resized.Result(ct);

    // Branch 1: Gallery view
    var galleryBranch = galleryResult.Branch();
    var galleryEntity = await PhotoGallery.Upload(galleryBranch, $"{photo.Id}_gallery.jpg", "image/jpeg", ct: ct);
    photo.GalleryMediaId = galleryEntity.Id;

    // Branch 2: Thumbnail (square crop)
    var thumbnailBranch = galleryResult.Branch();
    var cropped = await thumbnailBranch.CropSquare(ct: ct);
    var thumbnailStream = await cropped.ResizeFit(150, 150, ct);
    var thumbnailEntity = await PhotoThumbnail.Upload(thumbnailStream, $"{photo.Id}_thumb.jpg", "image/jpeg", ct: ct);

    photo.ThumbnailMediaId = thumbnailEntity.Id;
    await galleryResult.DisposeAsync();

    return photo;
}
```

The fluent API handles stream disposal automatically and enables efficient derivative generation.

### Step 3: Extract EXIF metadata

Capture camera metadata using ImageSharp 3.x:

```csharp
private async Task ExtractExifMetadataAsync(PhotoAsset photo, Stream stream, CancellationToken ct)
{
    stream.Position = 0;
    using var image = await Image.LoadAsync(stream, ct);
    var exif = image.Metadata.ExifProfile;
    if (exif == null) return;

    // Camera information
    if (exif.TryGetValue(ExifTag.Model, out var modelValue))
        photo.CameraModel = modelValue.Value?.ToString();

    if (exif.TryGetValue(ExifTag.LensModel, out var lensValue))
        photo.LensModel = lensValue.Value?.ToString();

    // Capture settings
    if (exif.TryGetValue(ExifTag.FocalLength, out var focalLengthValue))
        photo.FocalLength = $"{focalLengthValue.Value}mm";

    if (exif.TryGetValue(ExifTag.ISOSpeedRatings, out var isoValue) &&
        isoValue.Value is ushort[] isoArray && isoArray.Length > 0)
        photo.ISO = isoArray[0];

    // GPS coordinates
    if (exif.TryGetValue(ExifTag.GPSLatitude, out var latValue) &&
        exif.TryGetValue(ExifTag.GPSLongitude, out var lonValue) &&
        latValue.Value is Rational[] gpsLatitude &&
        lonValue.Value is Rational[] gpsLongitude)
    {
        photo.Location = new GpsCoordinates
        {
            Latitude = ConvertToDecimalDegrees(gpsLatitude),
            Longitude = ConvertToDecimalDegrees(gpsLongitude)
        };
    }

    // Capture timestamp
    if (exif.TryGetValue(ExifTag.DateTimeOriginal, out var dateValue) &&
        DateTime.TryParse(dateValue.Value?.ToString(), out var capturedAt))
    {
        photo.CapturedAt = DateTime.SpecifyKind(capturedAt, DateTimeKind.Utc);
    }
}
```

This preserves valuable metadata for organization and discovery.

### Step 4: Implement semantic search with vectors

Generate embeddings and enable semantic search:

```csharp
public async Task<PhotoAsset> GenerateAIMetadataAsync(PhotoAsset photo, CancellationToken ct)
{
    // Build embedding text from metadata
    var embeddingText = $"Filename: {photo.OriginalFileName}\n";
    if (photo.AutoTags.Any())
        embeddingText += $"Tags: {string.Join(", ", photo.AutoTags)}\n";
    if (!string.IsNullOrEmpty(photo.MoodDescription))
        embeddingText += $"Mood: {photo.MoodDescription}\n";

    // Generate embedding using Koan AI
    var embedding = await Ai.Embed(embeddingText, ct);

    // Prepare vector metadata for hybrid search
    var vectorMetadata = new Dictionary<string, object>
    {
        ["originalFileName"] = photo.OriginalFileName,
        ["eventId"] = photo.EventId,
        ["searchText"] = embeddingText
    };

    // Save with vector using framework pattern
    await Data<PhotoAsset, string>.SaveWithVector(photo, embedding, vectorMetadata, ct);

    return photo;
}

public async Task<List<PhotoAsset>> SemanticSearchAsync(string query, string? eventId = null, int topK = 20)
{
    // Generate query embedding
    var queryVector = await Ai.Embed(query, ct);

    // Check if vector search is available
    if (!Vector<PhotoAsset>.IsAvailable)
        return await FallbackKeywordSearch(query, eventId, topK, ct);

    // Perform hybrid vector search (50% semantic, 50% keyword)
    var vectorResults = await Vector<PhotoAsset>.Search(
        vector: queryVector,
        text: query,
        alpha: 0.5,
        topK: topK,
        ct: ct
    );

    // Load and filter photo entities
    var photos = new List<PhotoAsset>();
    foreach (var match in vectorResults.Matches)
    {
        var photo = await PhotoAsset.Get(match.Id, ct);
        if (photo != null && (string.IsNullOrEmpty(eventId) || photo.EventId == eventId))
            photos.Add(photo);
    }

    return photos;
}
```

This enables natural language search like "sunset at beach" or "family gathering".

### Step 5: Organize with event-based structure

Create hierarchical event organization:

```csharp
public class Event : Entity<Event>
{
    public string Name { get; set; } = "";
    public EventType Type { get; set; }
    public DateTime EventDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }

    public int PhotoCount { get; set; }
    public StorageTier CurrentTier { get; set; } = StorageTier.Hot;
    public ProcessingStatus ProcessingStatus { get; set; }
}

[HttpGet("timeline")]
public async Task<ActionResult<TimelineResponse>> GetTimeline(
    [FromQuery] int months = 12,
    [FromQuery] EventType? type = null,
    CancellationToken ct = default)
{
    var cutoffDate = DateTime.UtcNow.AddMonths(-months);
    var allEvents = await Event.Query(e => e.EventDate >= cutoffDate, ct);

    var events = allEvents;
    if (type.HasValue)
        events = events.Where(e => e.Type == type.Value).ToList();

    events = events.OrderByDescending(e => e.EventDate).ToList();

    // Group by month/year for timeline display
    var groups = events
        .GroupBy(e => new { e.EventDate.Year, e.EventDate.Month })
        .Select(g => new TimelineGroup
        {
            Year = g.Key.Year,
            Month = g.Key.Month,
            MonthName = new DateTime(g.Key.Year, g.Key.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .ToString("MMMM"),
            Events = g.ToList()
        })
        .ToList();

    return Ok(new TimelineResponse
    {
        Groups = groups,
        TotalEvents = events.Count,
        TotalPhotos = events.Sum(e => e.PhotoCount)
    });
}
```

Events provide natural grouping for photo collections.

## Key patterns and techniques demonstrated

**Fluent transformation pipelines (DX-0047)**
The new fluent API eliminates boilerplate operator loops. Transformations are declared as method chains with automatic resource management and clear intent.

**Multi-entity derivative pattern**
Separate entities for each derivative size enable optimal storage tier assignment and independent lifecycle management.

**Background AI processing**
Embedding generation runs asynchronously after upload completes, ensuring fast user response while building search capabilities over time.

**Hybrid search with graceful fallback**
Semantic vector search enhances discovery, but keyword fallback ensures functionality when AI services are unavailable.

**EXIF preservation**
Camera metadata enriches photo organization and enables queries like "all photos taken with Canon 5D" or "photos from this GPS location".

**SignalR progress tracking**
Real-time upload progress and processing status updates provide responsive feedback during batch operations.

## What you'll build by following this sample

**A complete photo management system** with event organization, automatic derivatives, and semantic search

**Modern image processing** using declarative fluent transformation pipelines

**AI integration patterns** that enhance search without creating dependencies

**Production-ready upload handling** with progress tracking, error recovery, and batch processing

## Prerequisites (managed automatically by start.bat)

```bash
# Required: MongoDB for data storage
docker run -d -p 27017:27017 mongo:latest

# Required: Weaviate for vector search
docker run -d -p 8080:8080 semitechnologies/weaviate:latest

# Optional: Ollama for local AI embeddings
docker run -d -p 11434:11434 ollama/ollama
ollama pull all-minilm
```

**Why these services?**

- **MongoDB** stores photo metadata, events, and relationships
- **Weaviate** provides vector similarity search for semantic photo discovery
- **Ollama** generates embeddings locally without external API dependencies

## Start the application (recommended)

```bash
start.bat
```

This automatically:
- Builds the Docker image
- Starts all required services (MongoDB, Weaviate, Ollama)
- Initializes the application
- Opens the browser to http://localhost:5086

**Try the features:**

1. Create an event (Wedding, Vacation, Birthday, etc.)
2. Upload photos to the event (supports batch upload)
3. Watch automatic thumbnail and gallery generation
4. Search semantically: "sunset at beach", "people laughing"
5. View event timeline grouped by month/year
6. Explore EXIF metadata (camera, GPS, settings)

## Understanding the code structure

**Controllers/** - API endpoints following Koan's EntityController pattern

```
EventsController.cs    - Event CRUD with timeline and tier management
PhotosController.cs    - Photo upload, search, and batch operations
```

**Services/** - Business logic for photo processing

```
PhotoProcessingService.cs  - Image transformation and EXIF extraction
IPhotoProcessingService.cs - Service interface
```

**Models/** - Entities and data contracts

```
PhotoAsset.cs      - Full-resolution photo entity (cold storage)
PhotoGallery.cs    - Gallery derivative entity (warm storage)
PhotoThumbnail.cs  - Thumbnail entity (hot-cdn storage)
Event.cs           - Event organization entity
ProcessingJob.cs   - Batch upload tracking
```

**wwwroot/** - Static web interface (to be implemented)

```
js/gallery.js      - Photo grid with virtual scrolling
js/timeline.js     - Event timeline visualization
js/upload.js       - Drag-drop upload with progress
js/search.js       - Semantic and filter-based search
```

## API reference

**Events** (`/api/events`)

- `GET /` - Query events with filtering
- `POST /query` - Advanced event queries
- `GET /{id}` - Get single event with photo count
- `POST /` - Create new event with validation
- `DELETE /{id}` - Delete event (photos must be removed first)
- `GET /timeline` - Get event timeline grouped by month/year
- `GET /by-tier/{tier}` - Get events by storage tier
- `POST /{id}/archive` - Move event to cold storage

**Photos** (`/api/photos`)

- `GET /` - Query photos across all events
- `POST /query` - Advanced photo queries
- `GET /{id}` - Get single photo with derivatives
- `DELETE /{id}` - Delete photo and all derivatives
- `POST /upload` - Upload photos with batch processing
- `POST /search` - Semantic search with hybrid scoring
- `GET /by-event/{eventId}` - Get photos for event with pagination
- `POST /{id}/favorite` - Toggle favorite status
- `GET /{id}/download` - Download full-resolution original

## Framework patterns demonstrated

**MediaEntity<T> with StorageBinding**
Declarative storage tier assignment using attributes eliminates manual profile configuration.

**DX-0047 Fluent Media Transform API**
Stream-based transformation with automatic disposal, branching for derivatives, and method chaining.

**Entity<T> base class**
Auto GUID v7 generation, transparent provider access, and relationship navigation.

**EntityController<T>**
Automatic REST endpoints with customizable overrides for business logic.

**Data<T,K> static API**
Direct entity access without repository injection, following framework conventions.

**Vector<T> semantic search**
Hybrid vector + keyword search with provider capability detection and fallback.

## Learning outcomes

**After exploring this sample, you'll understand:**

- How to process images using fluent transformation pipelines
- Techniques for multi-tier storage architecture with automatic tier assignment
- Patterns for extracting and preserving EXIF metadata
- Approaches to semantic search with vector embeddings and keyword fallback
- Methods for batch upload handling with progress tracking
- Strategies for organizing photos with event hierarchies
- Real-time progress updates using SignalR

**You'll also see practical implementations of:**

- Background AI processing for embedding generation
- Multi-entity derivative management
- Stream-based media transformation with automatic resource cleanup
- GPS coordinate extraction and conversion
- Image quality optimization and format conversion
- Graceful degradation when AI services are unavailable

This sample serves as both a working application and a reference implementation for building similar systems in your own projects.

## Technical highlights

**DX-0047 in action:**
```csharp
// Before: Manual operator loops (80+ lines)
Stream current = sourceStream;
foreach (var (op, pars) in operators) { /* complex disposal logic */ }

// After: Fluent API (3 lines)
var result = await photo.OpenRead()
    .AutoOrient()
    .ResizeFit(1200, 1200, ct);
```

**Multi-entity branching:**
```csharp
// Materialize once, create multiple derivatives efficiently
using var result = await photo.OpenRead().AutoOrient().Result();
var gallery = await result.Branch().ResizeFit(1200, 1200).StoreAs<PhotoGallery>();
var thumb = await result.Branch().CropSquare().Resize(150, 150).StoreAs<PhotoThumbnail>();
```

**Hybrid vector search:**
```csharp
// 50% semantic similarity, 50% keyword matching
var results = await Vector<PhotoAsset>.Search(
    vector: queryVector,
    text: query,
    alpha: 0.5,
    topK: 20,
    ct: ct
);
```

This sample demonstrates production-ready photo management with modern image processing, AI-powered search, and intelligent storage architecture.
