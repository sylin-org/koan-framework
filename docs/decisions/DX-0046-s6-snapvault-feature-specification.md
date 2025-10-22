---
type: DEV
domain: samples
title: "S6.SnapVault - Event Photography Platform Feature Specification"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-16
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-16
  status: approved
  scope: samples/S6.SnapVault
related_adrs: [DX-0046-IMPLEMENTATION]
---

# DX-0046: S6.SnapVault Feature Specification

Status: Approved

**Implementation Guide**: See [DX-0046-IMPLEMENTATION.md](./DX-0046-IMPLEMENTATION.md) for detailed framework integration patterns, architectural decisions, and implementation roadmap.

## Overview

**S6.SnapVault** is an event photography platform demonstrating Koan's media processing, storage tiering, backup/restore, and AI capabilities. Photographers upload event photos (weddings, conferences, graduations), which are processed into multiple formats, intelligently stored across hot/warm/cold tiers, and made searchable through AI-powered semantic search.

**Domain**: Professional event photography management and client galleries

**Tagline**: *"Professional event photography platform with intelligent storage management"*

**Primary Framework Capabilities Demonstrated**:
- ✅ Koan.Media.* - MediaEntity<T>, MediaOperators (ResizeOperator, RotateOperator)
- ✅ Koan.Storage.* - Multi-profile storage, tier migration via IStorageService
- ✅ Koan.Data.Backup - IBackupService for backup and restore workflows
- ✅ Koan.AI.Vision - IAIVisionService for computer vision
- ✅ Koan.Data.Vector.* - [Vector] attribute and semantic search
- ✅ Entity<T> patterns - Event, ProcessingJob (metadata entities)
- ✅ MediaEntity<T> patterns - PhotoAsset, derivatives (storage-backed media)
- ✅ [StorageBinding] - Entity-level storage profile configuration

**Timeline**: 5-6 weeks (reduced from 6.5 weeks due to framework usage)

---

## Core Features

### 1. Event Management

#### 1.1 Create Event
**User Story**: As a photographer, I want to create an event to organize photos by occasion.

**Fields**:
- Event name (required)
- Event type (Wedding, Corporate, Birthday, Graduation, Anniversary, Other)
- Event date (required)
- Client name (optional)
- Location (optional)
- Description (optional)
- Password protection (optional - for client gallery access)

**Business Rules**:
- Event date triggers automatic archival workflow (3 months → warm, 12 months → cold)
- Each event gets unique shareable link
- Password-protected events require authentication for client access

**Entity Model**:
```csharp
public class Event : Entity<Event>
{
    public string Name { get; set; } = "";
    public EventType Type { get; set; }
    public DateTime EventDate { get; set; }
    public string? ClientName { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? GalleryPassword { get; set; }
    public DateTime CreatedAt { get; set; }

    // Computed
    public int PhotoCount { get; set; }
    public StorageTier CurrentTier { get; set; } // Hot, Warm, Cold
    public long TotalStorageBytes { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
}

public enum EventType
{
    Wedding,
    Corporate,
    Birthday,
    Graduation,
    Anniversary,
    Other
}

public enum StorageTier
{
    Hot,    // Recent events, frequent access, CDN-backed
    Warm,   // Older events, occasional access
    Cold    // Archived events, rare access, cost-optimized
}
```

---

#### 1.2 Upload Photos to Event
**User Story**: As a photographer, I want to upload multiple photos to an event for processing.

**Features**:
- Drag-and-drop upload
- Multi-file selection (20-500 photos per batch)
- Progress indicator per file and overall
- EXIF data extraction (capture date, camera model, GPS, etc.)
- Supported formats: JPEG, PNG, RAW (CR2, NEF, ARW - converted to JPEG)
- Max file size: 25MB per photo
- Auto-cancel on errors with clear feedback

**Processing Pipeline**:
```
Upload → EXIF Extract → AI Analysis → Generate Sizes → Store → Index
```

**Background Processing** (via Koan.Scheduling or message queue):
1. Extract EXIF metadata (camera, lens, settings, GPS)
2. AI vision analysis (object detection, mood description)
3. Generate image sizes:
   - Thumbnail: 150x150px (~10-15KB)
   - Gallery: 1200px longest edge (~150-200KB)
   - Full-res: Original (5-8MB)
4. Store in appropriate tier
5. Generate embeddings for semantic search
6. Update event counters

**Entity Model** (simplified for spec - see [DX-0046-IMPLEMENTATION.md](./DX-0046-IMPLEMENTATION.md) for detailed multi-entity architecture):
```csharp
/// <summary>
/// Full-resolution photo asset with AI metadata and vector embeddings
/// Uses multi-entity pattern: PhotoAsset (full-res), PhotoGallery (1200px), PhotoThumbnail (150x150)
/// Each derivative uses [StorageBinding] for tier-specific storage
/// </summary>
[StorageBinding(Profile = "cold", Container = "photos-fullres")]
public class PhotoAsset : MediaEntity<PhotoAsset>  // MediaEntity, not Entity!
{
    public string EventId { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; } // From EXIF

    // Derived media references (framework MediaEntity pattern)
    public string? GalleryMediaId { get; set; }  // References PhotoGallery.Id
    // ThumbnailMediaId inherited from MediaEntity<T>

    // EXIF metadata
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public string? FocalLength { get; set; }
    public string? Aperture { get; set; }
    public string? ShutterSpeed { get; set; }
    public int? ISO { get; set; }
    public GpsCoordinates? Location { get; set; }

    // AI-generated metadata
    public List<string> DetectedObjects { get; set; } = new();
    public string MoodDescription { get; set; } = "";
    public List<string> AutoTags { get; set; } = new();

    // Vector for semantic search
    [Vector(Model = "text-embedding-3-small")]
    public float[]? Embedding { get; set; }

    // Stats
    public int ViewCount { get; set; }
    public bool IsFavorite { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
}

/// <summary>
/// Gallery-size derivative (1200px) - warm tier storage
/// </summary>
[StorageBinding(Profile = "warm", Container = "photos-gallery")]
public class PhotoGallery : MediaEntity<PhotoGallery>
{
    // SourceMediaId inherited from MediaEntity - points to PhotoAsset.Id
    // DerivationKey inherited - set to "gallery-1200"
}

/// <summary>
/// Thumbnail derivative (150x150) - hot tier with CDN
/// </summary>
[StorageBinding(Profile = "hot-cdn", Container = "photos-thumbnails")]
public class PhotoThumbnail : MediaEntity<PhotoThumbnail>
{
    // SourceMediaId inherited from MediaEntity - points to PhotoAsset.Id
    // DerivationKey inherited - set to "thumbnail-150"
}

public class ProcessingJob : Entity<ProcessingJob>
{
    public string EventId { get; set; }
    public ProcessingStatus Status { get; set; }
    public int TotalPhotos { get; set; }
    public int ProcessedPhotos { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> Errors { get; set; } = new();
}

public enum ProcessingStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartialSuccess
}
```

---

### 2. Grid Gallery Mode (Primary View)

**User Story**: As a client, I want to browse event photos in a beautiful grid layout.

**Features**:
- **Virtual scrolling** (handle 500+ photos without performance degradation)
- **Lazy loading** (load thumbnails as they enter viewport)
- **Layout toggle**: Grid view (default) or Compact list view
- **Lightbox viewer**:
  - Full-screen gallery-size image
  - Next/previous navigation (keyboard arrows)
  - Zoom capability
  - Download full-res button
  - Favorite toggle
  - Photo metadata sidebar (EXIF, AI tags)
- **Select favorites**: Multi-select with bulk actions
- **Download zip**: Download selected favorites as zip file
- **Share**: Share individual photo link or gallery link

**Performance Requirements**:
- Initial page load: <2 seconds (first 20 thumbnails)
- Scroll performance: 60fps maintained
- Lightbox open: <500ms
- Virtual scrolling buffer: 20 photos above/below viewport

**UI Components**:
```javascript
// Virtual scrolling grid
<div class="gallery-grid">
  <!-- Only render visible + buffer photos -->
  <div class="photo-card" *ngFor="let photo of visiblePhotos">
    <img [src]="photo.thumbnailUrl"
         loading="lazy"
         (click)="openLightbox(photo)">
    <div class="photo-actions">
      <button (click)="toggleFavorite(photo)">❤️</button>
      <span>{{photo.autoTags[0]}}</span>
    </div>
  </div>
</div>

// Lightbox
<div class="lightbox" *ngIf="selectedPhoto">
  <img [src]="selectedPhoto.galleryUrl">
  <div class="metadata-sidebar">
    <h3>Photo Details</h3>
    <p><strong>Camera:</strong> {{selectedPhoto.cameraModel}}</p>
    <p><strong>Mood:</strong> {{selectedPhoto.moodDescription}}</p>
    <p><strong>Tags:</strong> {{selectedPhoto.autoTags.join(', ')}}</p>
    <button (click)="downloadFullRes()">Download Full-Res</button>
  </div>
</div>
```

---

### 3. Event Timeline Mode (Secondary View)

**User Story**: As a photographer, I want to see event management overview with processing status and storage metrics.

**Features**:
- **Date-based grouping**: Events grouped by month/year
- **Event cards** showing:
  - Event name, type, date
  - Photo count
  - Processing status (pending, in-progress, completed)
  - Storage tier distribution (pie chart or bar)
  - Total storage usage
  - Last accessed date
  - Backup status indicator
- **Expand event**: Show thumbnails grid + metadata
- **Bulk actions**:
  - Archive to cold storage
  - Trigger backup
  - Delete event (soft-delete)
- **Filters**:
  - By event type
  - By storage tier
  - By processing status
  - By date range

**Performance Requirements**:
- Initial load: <1 second (metadata only)
- Expand event: <300ms (lazy load thumbnails)
- Real-time updates: Via SignalR for processing jobs

**UI Components**:
```javascript
// Timeline view
<div class="timeline">
  <div class="timeline-group" *ngFor="let group of eventGroups">
    <h2>{{group.month}} {{group.year}}</h2>
    <div class="event-card" *ngFor="let event of group.events">
      <h3>{{event.name}}</h3>
      <div class="event-stats">
        <span>{{event.photoCount}} photos</span>
        <span class="tier-badge">{{event.currentTier}}</span>
        <span class="status-badge">{{event.processingStatus}}</span>
      </div>
      <div class="storage-chart">
        <!-- Pie chart: Hot/Warm/Cold distribution -->
      </div>
      <div class="actions">
        <button (click)="archiveEvent(event)">Archive</button>
        <button (click)="backupEvent(event)">Backup</button>
        <button (click)="expandEvent(event)">View Photos</button>
      </div>
    </div>
  </div>
</div>
```

---

### 4. AI-Powered Features

#### 4.1 Automatic Image Analysis
**User Story**: As a photographer, I want photos automatically analyzed so clients can search without manual tagging.

**Features**:
- **Object detection**: Identify people, objects, settings
  - Example: ["bride", "groom", "flowers", "outdoor", "sunset"]
- **Mood description**: Generate natural language description
  - Example: "Romantic outdoor wedding ceremony at sunset with floral decorations"
- **Auto-tagging**: Generate searchable tags
  - Example: ["wedding", "outdoor", "romantic", "sunset", "formal"]

**Implementation** (simplified - see [DX-0046-IMPLEMENTATION.md](./DX-0046-IMPLEMENTATION.md) for complete implementation):
```csharp
public class PhotoProcessingService
{
    private readonly IPhotoVisionService _visionService;
    private readonly ResizeOperator _resizeOperator;  // Framework MediaOperator
    private readonly RotateOperator _rotateOperator;  // Framework MediaOperator

    public async Task<PhotoAsset> ProcessUploadAsync(string eventId, IFormFile file, CancellationToken ct)
    {
        using var stream = file.OpenReadStream();

        // Step 1: Auto-orient using EXIF (framework operator)
        var orientedStream = await _rotateOperator.ExecuteAsync(stream, new(), ct);

        // Step 2: Upload full-res photo (MediaEntity upload)
        var photo = await PhotoAsset.Upload(orientedStream, file.FileName, file.ContentType);
        photo.EventId = eventId;

        // Step 3: Extract EXIF, generate derivatives, run AI analysis
        // (see DX-0046-IMPLEMENTATION.md for complete pipeline)

        await photo.Save();
        return photo;
    }
}
```

**Configuration**:
```json
{
  "Koan": {
    "AI": {
      "Vision": {
        "Provider": "openai",
        "Model": "gpt-4-vision-preview",
        "MaxConcurrentRequests": 5
      }
    }
  }
}
```

---

#### 4.2 Semantic Search
**User Story**: As a client, I want to search photos using natural language instead of browsing folders.

**Features**:
- **Natural language queries**:
  - "sunset photos"
  - "romantic moments"
  - "outdoor ceremony"
  - "bride and groom portrait"
- **Real-time search** (as-you-type with debounce)
- **Search results** show:
  - Relevance score
  - Matching tags highlighted
  - Quick preview on hover
- **Filters**:
  - Combine semantic search with filters (date range, favorites only)

**Implementation**:
```csharp
[HttpPost("api/photos/search")]
public async Task<ActionResult<SearchResponse>> SearchPhotos([FromBody] SearchRequest request)
{
    // Koan semantic search - one line
    var photos = await PhotoAsset.SemanticSearch(
        query: request.Query,
        topK: request.Limit,
        filter: p => p.EventId == request.EventId // Optional: scope to event
    );

    return Ok(new SearchResponse
    {
        Photos = photos,
        Query = request.Query,
        ResultCount = photos.Count
    });
}
```

**UI Component**:
```javascript
// Search bar with debounce
<div class="search-bar">
  <input type="text"
         placeholder="Search photos... (e.g., 'sunset moments')"
         [(ngModel)]="searchQuery"
         (input)="onSearchInput()">
  <button (click)="clearSearch()">Clear</button>
</div>

<div class="search-results" *ngIf="searchResults.length > 0">
  <p>Found {{searchResults.length}} photos matching "{{searchQuery}}"</p>
  <div class="results-grid">
    <div class="photo-card" *ngFor="let photo of searchResults">
      <img [src]="photo.thumbnailUrl" (click)="openLightbox(photo)">
      <span class="relevance">{{photo.relevanceScore | percent}}</span>
      <div class="matching-tags">
        <span *ngFor="let tag of photo.autoTags">{{tag}}</span>
      </div>
    </div>
  </div>
</div>
```

**Vector Configuration**:
```json
{
  "Koan": {
    "Data": {
      "Vector": {
        "Provider": "qdrant",
        "Endpoint": "http://localhost:6333",
        "CollectionPrefix": "snapvault"
      }
    }
  }
}
```

---

### 5. Storage Tiering & Management

#### 5.1 Automatic Tier Aging
**User Story**: As a system administrator, I want photos automatically moved to cheaper storage as events age.

**Tiering Rules**:
- **Hot (CDN-backed)**: Events < 3 months old, or accessed in last 30 days
- **Warm**: Events 3-12 months old
- **Cold**: Events > 12 months old, or explicitly archived

**Implementation**:
```csharp
public class StorageTieringService
{
    public async Task RunTieringWorkflow()
    {
        var events = await Event.All();

        foreach (var evt in events)
        {
            var ageMonths = (DateTime.UtcNow - evt.EventDate).TotalDays / 30;
            var daysSinceAccess = (DateTime.UtcNow - evt.LastAccessedAt).TotalDays;

            StorageTier targetTier = DetermineTier(ageMonths, daysSinceAccess);

            if (evt.CurrentTier != targetTier)
            {
                await MigrateEventToTier(evt, targetTier);
            }
        }
    }

    private StorageTier DetermineTier(double ageMonths, double daysSinceAccess)
    {
        if (daysSinceAccess < 30 || ageMonths < 3)
            return StorageTier.Hot;
        else if (ageMonths < 12)
            return StorageTier.Warm;
        else
            return StorageTier.Cold;
    }

    private async Task MigrateEventToTier(Event evt, StorageTier tier)
    {
        var photos = await PhotoAsset.Query(p => p.EventId == evt.Id);

        foreach (var photo in photos)
        {
            // Koan.Storage handles tier migration
            await Storage.MigrateTier(photo.GalleryUrl, GetStorageProfile(tier));
            await Storage.MigrateTier(photo.FullResUrl, GetStorageProfile(tier));
            // Thumbnails always stay hot (small, frequently accessed)
        }

        evt.CurrentTier = tier;
        await evt.Save();

        _logger.LogInformation("Migrated event {EventId} to {Tier}", evt.Id, tier);
    }
}
```

**Scheduled Job** (Koan.Scheduling):
```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddKoan()
            .AsWebApi()
            .AddScheduling(scheduler =>
            {
                // Run tier aging workflow daily at 2 AM
                scheduler.Schedule<StorageTieringService>(
                    service => service.RunTieringWorkflow(),
                    cron: "0 2 * * *"
                );
            });
    }
}
```

---

#### 5.2 Manual Archive
**User Story**: As a photographer, I want to manually archive old events to save costs.

**Features**:
- Archive single event or bulk archive
- Preview storage savings before archiving
- Confirmation dialog showing:
  - Current storage tier distribution
  - Estimated savings ($/month)
  - Archive time estimate
- Background job with progress indicator
- Reversible (can un-archive to warm tier)

**UI Component**:
```javascript
<button (click)="showArchiveDialog(event)">Archive Event</button>

<dialog class="archive-dialog" *ngIf="showDialog">
  <h3>Archive Event: {{event.name}}</h3>
  <p>This will move all photos to cold storage.</p>

  <div class="storage-preview">
    <div class="current">
      <h4>Current</h4>
      <p>Hot: 2.5 GB ($10/month)</p>
      <p>Warm: 1.2 GB ($3/month)</p>
    </div>
    <div class="after-archive">
      <h4>After Archive</h4>
      <p>Cold: 3.7 GB ($0.50/month)</p>
      <p class="savings">Save $12.50/month</p>
    </div>
  </div>

  <button (click)="confirmArchive()">Archive</button>
  <button (click)="cancelArchive()">Cancel</button>
</dialog>
```

---

### 6. Backup & Restore

#### 6.1 Automatic Backups
**User Story**: As a photographer, I want photos automatically backed up so I never lose client data.

**Features**:
- **Scheduled backups**: Daily incremental, weekly full
- **Backup targets**:
  - Cloud storage (S3, Azure Blob, GCS)
  - Local NAS (optional)
- **Backup scope**:
  - Full-res images (always)
  - Gallery images (optional, save bandwidth)
  - Thumbnails (no, regeneratable)
  - Metadata + EXIF (always)
- **Backup verification**: Checksum validation
- **Retention policy**: Keep last 30 daily, 12 weekly, 24 monthly

**Implementation**:
```csharp
public class BackupService
{
    public async Task BackupEvent(string eventId)
    {
        var evt = await Event.Get(eventId);
        var photos = await PhotoAsset.Query(p => p.EventId == eventId);

        var backupJob = new BackupJob
        {
            EventId = eventId,
            StartedAt = DateTime.UtcNow,
            TotalItems = photos.Count,
            Type = BackupType.Full
        };
        await backupJob.Save();

        try
        {
            // Koan.Data.Backup - handles streaming, compression, verification
            await Data.Backup.CreateAsync(new BackupOptions
            {
                Name = $"event-{eventId}-{DateTime.UtcNow:yyyyMMdd}",
                Items = photos.Select(p => new BackupItem
                {
                    Id = p.Id,
                    Data = await Storage.ReadAsync(p.FullResUrl),
                    Metadata = new Dictionary<string, string>
                    {
                        ["eventId"] = eventId,
                        ["capturedAt"] = p.CapturedAt?.ToString("o") ?? "",
                        ["moodDescription"] = p.MoodDescription
                    }
                }),
                Compression = CompressionLevel.Optimal,
                Encryption = true,
                Target = BackupTarget.Cloud
            });

            backupJob.Status = BackupStatus.Completed;
            backupJob.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            backupJob.Status = BackupStatus.Failed;
            backupJob.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Backup failed for event {EventId}", eventId);
        }
        finally
        {
            await backupJob.Save();
        }
    }
}

// BackupJob entity REMOVED - using framework IBackupService instead
// See DX-0046-IMPLEMENTATION.md for IEventBackupService integration
```

**Scheduled Backup**:
```csharp
services.AddScheduling(scheduler =>
{
    // Full backup of all events - daily at 1 AM
    scheduler.Schedule<BackupService>(
        service => service.BackupAllEvents(BackupType.Full),
        cron: "0 1 * * *"
    );
});
```

---

#### 6.2 Restore from Backup
**User Story**: As a photographer, I want to restore an event from backup if local data is lost.

**Features**:
- **Browse backups**: List available backups by event and date
- **Preview backup**: Show metadata before restoring
- **Restore options**:
  - Restore to new event (don't overwrite)
  - Restore in place (replace existing)
  - Selective restore (specific photos only)
- **Progress tracking**: Real-time restore progress
- **Verification**: Checksum validation after restore

**UI Component**:
```javascript
<div class="backup-browser">
  <h3>Available Backups for {{event.name}}</h3>
  <table>
    <thead>
      <tr>
        <th>Date</th>
        <th>Type</th>
        <th>Size</th>
        <th>Photos</th>
        <th>Actions</th>
      </tr>
    </thead>
    <tbody>
      <tr *ngFor="let backup of backups">
        <td>{{backup.createdAt | date}}</td>
        <td>{{backup.type}}</td>
        <td>{{backup.sizeBytes | fileSize}}</td>
        <td>{{backup.photoCount}}</td>
        <td>
          <button (click)="previewBackup(backup)">Preview</button>
          <button (click)="restoreBackup(backup)">Restore</button>
        </td>
      </tr>
    </tbody>
  </table>
</div>

<dialog *ngIf="restoreInProgress">
  <h3>Restoring from Backup</h3>
  <progress [value]="restoreProgress" max="100"></progress>
  <p>{{restoreProgress}}% complete ({{processedPhotos}}/{{totalPhotos}} photos)</p>
</dialog>
```

**Implementation**:
```csharp
[HttpPost("api/events/{eventId}/restore")]
public async Task<ActionResult> RestoreEvent(string eventId, [FromBody] RestoreRequest request)
{
    var backupJob = await BackupJob.Get(request.BackupJobId);

    // Koan.Data.Backup - streaming restore with verification
    var restoreResult = await Data.Backup.RestoreAsync(new RestoreOptions
    {
        BackupName = backupJob.BackupName,
        VerifyChecksums = true,
        OnProgress = (processed, total) =>
        {
            // Real-time progress via SignalR
            _hubContext.Clients.Group(eventId).SendAsync("RestoreProgress", new
            {
                Processed = processed,
                Total = total,
                Percentage = (int)((processed / (double)total) * 100)
            });
        }
    });

    // Recreate entities from backup
    foreach (var item in restoreResult.Items)
    {
        var photo = new PhotoAsset
        {
            Id = item.Id,
            EventId = eventId,
            CapturedAt = DateTime.Parse(item.Metadata["capturedAt"]),
            MoodDescription = item.Metadata["moodDescription"],
            // ... restore all fields
        };

        // Store full-res, regenerate thumbnails/gallery sizes
        photo.FullResUrl = await Storage.WriteAsync(item.Data, "cold");
        await RegenerateImageSizes(photo);

        await photo.Save();
    }

    return Ok(new { Message = "Restore completed", PhotoCount = restoreResult.Items.Count });
}
```

---

### 7. Dashboard & Analytics

**User Story**: As a photographer, I want to see business metrics and system health.

**Dashboard Widgets**:

**Storage Overview**:
- Total storage used (GB)
- Tier distribution (pie chart):
  - Hot: X GB ($Y/month)
  - Warm: X GB ($Y/month)
  - Cold: X GB ($Y/month)
- Storage cost trend (last 6 months)
- Storage savings from tiering

**Event Metrics**:
- Total events
- Active events (last 3 months)
- Archived events
- Total photos uploaded
- Processing queue status

**Client Activity**:
- Gallery views (last 30 days)
- Photo downloads
- Most viewed events
- Search queries (popular terms)

**System Health**:
- Processing queue depth
- Failed processing jobs
- Backup status (last backup time, next scheduled)
- AI service health
- Storage health

**Implementation**:
```csharp
[HttpGet("api/dashboard")]
public async Task<ActionResult<DashboardData>> GetDashboard()
{
    var events = await Event.All();
    var photos = await PhotoAsset.Query(p => true); // All photos

    return Ok(new DashboardData
    {
        StorageOverview = new StorageOverview
        {
            TotalGB = photos.Sum(p => p.FileSizeBytes) / 1024.0 / 1024 / 1024,
            HotGB = photos.Where(p => /* in hot tier */).Sum(...),
            WarmGB = photos.Where(p => /* in warm tier */).Sum(...),
            ColdGB = photos.Where(p => /* in cold tier */).Sum(...),
            MonthlyCost = CalculateMonthlyCost(photos)
        },
        EventMetrics = new EventMetrics
        {
            TotalEvents = events.Count,
            ActiveEvents = events.Count(e => e.EventDate > DateTime.UtcNow.AddMonths(-3)),
            TotalPhotos = photos.Count,
            ProcessingQueueDepth = await GetProcessingQueueDepth()
        },
        ClientActivity = new ClientActivity
        {
            GalleryViews = await GetRecentViews(30),
            PopularSearchTerms = await GetPopularSearches(30)
        },
        SystemHealth = new SystemHealth
        {
            LastBackup = await GetLastBackupTime(),
            FailedJobs = await GetFailedJobCount(),
            AIServiceStatus = await CheckAIServiceHealth()
        }
    });
}
```

---

## API Endpoints

### Events
```
POST   /api/events                    Create event
GET    /api/events                    List all events
GET    /api/events/{id}               Get event details
PUT    /api/events/{id}               Update event
DELETE /api/events/{id}               Soft-delete event
POST   /api/events/{id}/photos        Upload photos to event
POST   /api/events/{id}/archive       Archive event to cold storage
POST   /api/events/{id}/backup        Trigger backup
POST   /api/events/{id}/restore       Restore from backup
```

### Photos
```
GET    /api/photos                    List photos (with pagination)
GET    /api/photos/{id}               Get photo details
POST   /api/photos/search             Semantic search
DELETE /api/photos/{id}               Delete photo
POST   /api/photos/{id}/favorite      Toggle favorite
GET    /api/photos/{id}/download      Download full-res
POST   /api/photos/batch/download     Download multiple as zip
```

### Processing
```
GET    /api/processing/jobs           List processing jobs
GET    /api/processing/jobs/{id}      Get job status
POST   /api/processing/jobs/{id}/retry Retry failed job
```

### Dashboard
```
GET    /api/dashboard                 Get dashboard metrics
GET    /api/dashboard/storage-trend   Storage cost over time
```

### Gallery (Client View)
```
GET    /api/gallery/{eventId}         Get public gallery (password check)
POST   /api/gallery/{eventId}/auth    Authenticate with password
```

---

## Non-Functional Requirements

### Performance
- **Upload throughput**: 10 photos/minute minimum (limited by AI processing)
- **Search latency**: <500ms for semantic search (20 results)
- **Gallery load**: <2s for 500 photos (virtual scrolling)
- **Lightbox open**: <500ms
- **Backup speed**: 1GB/minute minimum
- **Tier migration**: Background, no user-visible latency

### Scalability
- **Photos per event**: Up to 1000
- **Total events**: Up to 500
- **Total photos**: Up to 100,000
- **Concurrent uploads**: 3 photographers max
- **Concurrent gallery viewers**: 50 clients max

### Reliability
- **Backup success rate**: 99.9%
- **Processing success rate**: 95% (AI failures are acceptable)
- **Uptime**: 99% (sample application, not production SLA)

### Security
- **Gallery access**: Password-protected or authenticated
- **Photo downloads**: Rate limited (10/minute per IP)
- **Upload validation**: File type, size, malware scan
- **Backup encryption**: AES-256

---

## Technology Stack

### Backend
- ASP.NET Core 8.0+
- Koan.Media.Core, Koan.Media.Abstractions
- Koan.Storage.Core, Koan.Storage.Profiles
- Koan.Data.Backup
- Koan.AI.Vision (OpenAI connector)
- Koan.Data.Vector.Connector.Qdrant
- Koan.Scheduling
- SignalR (progress updates)

### Frontend
- Vanilla JavaScript + modern ES6 patterns
- CSS Grid + Flexbox
- Intersection Observer API (lazy loading)
- Virtual scrolling (custom implementation or library)
- Chart.js (dashboard visualizations)

### Infrastructure
- MongoDB (metadata, entity storage)
- Qdrant (vector database for semantic search)
- Object Storage (S3-compatible or local filesystem)
- OpenAI API (or compatible - vision analysis)
- Docker Compose (orchestration)

---

## Sample Data

### Seed 5 Events
1. **Sarah & Michael's Wedding** (Wedding, 2 months ago, 350 photos)
   - Tags: outdoor, romantic, sunset, formal, ceremony, reception
2. **Tech Summit 2024** (Corporate, 4 months ago, 180 photos)
   - Tags: conference, indoor, presentations, networking, technology
3. **Emma's Sweet 16** (Birthday, 8 months ago, 120 photos)
   - Tags: party, indoor, celebration, decorations, cake
4. **State University Graduation** (Graduation, 14 months ago, 280 photos)
   - Tags: outdoor, ceremony, formal, caps and gowns, family
5. **Johnson 50th Anniversary** (Anniversary, 18 months ago, 95 photos)
   - Tags: indoor, celebration, family, formal, decorations

**Storage Tier Distribution**:
- Events 1-2: Hot tier (recent)
- Event 3: Warm tier (4-12 months old)
- Events 4-5: Cold tier (>12 months old)

---

## Success Criteria

### Framework Demonstration
✅ Shows Koan.Media image processing pipeline
✅ Demonstrates Koan.Storage tiering (hot/warm/cold)
✅ Showcases Koan.Data.Backup workflows
✅ Highlights Koan.AI.Vision integration
✅ Proves Koan.Data.Vector semantic search
✅ Uses Entity<T> patterns throughout
✅ Demonstrates batch operations at scale

### Developer Experience
✅ One-command start (./start.bat)
✅ Comprehensive README (S5.Recs template)
✅ Clear code comments
✅ Testing examples included
✅ Performance benchmarks documented

### User Experience
✅ Beautiful, responsive UI
✅ 60fps scrolling with 500+ photos
✅ Natural language search works intuitively
✅ Clear feedback on all operations
✅ Backup/restore inspires confidence

---

## Implementation Phases

### Phase 1: Core Infrastructure (Week 1-1.5) ⚡ Accelerated
- Entity models (Event, PhotoAsset, PhotoGallery, PhotoThumbnail, ProcessingJob)
- **Framework Integration**: MediaEntity<T>, [StorageBinding], MediaOperators
- Upload pipeline using IPhotoProcessingService
- IPhotoStorage abstraction for multi-tier storage
- IEventBackupService wrapping IBackupService
- Basic API endpoints (CRUD for events/photos)

### Phase 2: Grid Gallery Mode (Week 3)
- Virtual scrolling implementation
- Lazy loading with Intersection Observer
- Lightbox viewer
- Layout toggle (grid/list)
- Favorite marking

### Phase 3: AI Features (Week 3.75-4.5) ⚡ Accelerated
- **Framework Integration**: IAIVisionService, IEmbeddingService
- IPhotoVisionService wrapping framework AI
- [Vector] attribute for semantic search
- PhotoAsset.SemanticSearch() endpoint
- Search UI component

### Phase 4: Event Timeline Mode (Week 5)
- Timeline grouping by date
- Event cards with metrics
- Processing status display
- Storage tier visualization
- Manual archive functionality

### Phase 5: Backup & Dashboard (Week 5.75-6.5) ⚡ Accelerated
- **Framework Integration**: IBackupService (replaces custom BackupJob)
- Automatic backup scheduling (Koan.Scheduling)
- Restore functionality using IEventBackupService
- Dashboard metrics
- Storage tier aging workflow using IStorageService

### Phase 6: Polish & Documentation (Week 6.5-7)
- Performance optimization pass
- Comprehensive README (S5.Recs template)
- Testing examples
- Docker Compose setup (MongoDB, Qdrant, OpenAI)
- Demo video/screenshots

**Total Timeline**: 5-6 weeks (reduced from 6.5 weeks due to framework usage)

---

## Out of Scope (Future Enhancements)

❌ Social media sharing integration
❌ Advanced image editing (filters, crops, adjustments)
❌ Client annotations/comments on photos
❌ Payment/e-commerce (selling prints)
❌ Mobile app (web-only for sample)
❌ Multi-photographer collaboration
❌ Video support (photos only)
❌ Face recognition
❌ Duplicate detection
❌ RAW format processing (convert to JPEG for sample)

---

## References

- **DX-0045**: Sample collection strategic realignment
- **DX-0044**: Adapter benchmark sample (SignalR progress pattern)
- **S5.Recs**: README template and AI integration patterns
- **S14.AdapterBench**: Real-time progress via SignalR
- **S16.PantryPal**: Vision AI integration patterns

---

**Status**: Approved for implementation
**Timeline**: 5.5-6.5 weeks (October 2025)
**Port Allocation**: 5094 (block 5090-5099)
**Sample Number**: S6
**Sample Name**: SnapVault
