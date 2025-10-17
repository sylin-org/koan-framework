---
type: DEV
domain: samples
title: "S6.SnapVault - Implementation Guide with Framework Integration"
audience: [developers, ai-agents]
status: current
last_updated: 2025-10-16
framework_version: v0.6.3
parent_adr: DX-0046
---

# DX-0046-IMPLEMENTATION: S6.SnapVault Implementation Guide

**Purpose**: This document enriches [DX-0046](./DX-0046-s6-snapvault-feature-specification.md) with framework research findings, architectural decisions, and implementation patterns. Use this guide for systematic implementation following Koan Framework best practices.

**Usage**: `Continue with implementation of S6.SnapVault, documented in DX-0046-IMPLEMENTATION.md`

---

## Framework Capabilities Research Summary

### Critical Findings

✅ **MediaEntity<T> Exists**: Framework provides `MediaEntity<T>` base class for storage-backed media entities
- Location: `Koan.Media.Abstractions/Model/MediaEntity.cs`
- Inherits from `StorageEntity<T>`, which inherits from `Entity<T>`
- Provides: Key, Name, ContentType, Size, ContentHash, SourceMediaId, DerivationKey, ThumbnailMediaId
- Static API: `MediaEntity<T>.Upload(stream, name, contentType, tags, ct)`

✅ **IBackupService Exists**: Framework provides enterprise-grade backup
- Location: `Koan.Data.Backup/Abstractions/IBackupService.cs`
- Methods: `BackupEntityAsync<TEntity, TKey>()`, `RestoreEntityAsync<TEntity, TKey>()`, `GetBackupProgressAsync()`
- Features: Streaming, ZIP archives, progress tracking, manifest generation
- **Decision**: Remove custom `BackupJob` entity, use IBackupService directly

✅ **MediaOperators Exist**: Framework provides image processing operators
- Location: `Koan.Media.Core/Operators/`
- **ResizeOperator**: Parameters: `w`, `h`, `fit`, `quality`, `upscale`
- **RotateOperator**: EXIF auto-orient with `image.Mutate(x => x.AutoOrient())`
- **TypeConverterOperator**: Format conversion
- Usage: `var result = await resizeOperator.ExecuteAsync(sourceMedia, parameters, ct);`

✅ **IStorageService Exists**: Framework provides multi-profile storage with tier migration
- Location: `Koan.Storage/Abstractions/IStorageService.cs`
- Key Method: `TransferToProfileAsync(sourceProfile, sourceContainer, key, targetProfile, ...)`
- Supports presigned URLs: `PresignReadAsync(profile, container, key, duration, ct)`

✅ **StorageEntity<T> Exists**: Base class for storage-backed entities
- Location: `Koan.Storage/Model/StorageEntity.cs`
- Methods: `Onboard(stream, name, contentType, tags, ct)`, `Create(...)`, `OpenRead(ct)`
- Properties: Key, Name, ContentType, Size, ContentHash

✅ **[StorageBinding] Attribute Exists**: Entity-level storage profile configuration
- Location: `Koan.Storage/Infrastructure/StorageBindingAttribute.cs`
- Usage: `[StorageBinding(Profile = "hot-cdn", Container = "thumbnails")]`
- Applies at class level, not property level

❌ **[StorageProfile] Attribute Does NOT Exist**: Property-level storage profile binding not available
- **Implication**: Cannot use `[StorageProfile("hot-cdn")]` on individual properties
- **Need Decision**: How to handle multi-tier storage for PhotoAsset (3 storage variants)

### Reference Implementation

**S16.PantryPal** provides excellent patterns for media + AI integration:
- Location: `samples/S16.PantryPal/Services/`
- **IPhotoStorage**: Abstraction over `IStorageService` with convenience methods
- **IPhotoVisionService**: Wraps `IAIVisionService` with domain-specific logic
- **Pattern**: Domain service layer → Framework services → Provider implementations

---

## Entity Architecture Decisions

### Decision Matrix: Entity<T> vs MediaEntity<T>

| Entity | Current Base | Correct Base | Rationale |
|--------|-------------|--------------|-----------|
| **Event** | Entity<Event> | ✅ Entity<Event> | Metadata entity, not storage-backed |
| **PhotoAsset** | Entity<PhotoAsset> | ❌ MediaEntity<PhotoAsset> | Storage-backed media - needs MediaEntity |
| **ProcessingJob** | Entity<ProcessingJob> | ✅ Entity<ProcessingJob> | Job tracking metadata, not storage |
| **BackupJob** | Entity<BackupJob> | ❌ REMOVE ENTITY | Replace with IBackupService API |

### Revised Entity Models

#### Event.cs (No Changes Required)
```csharp
using Koan.Data.Core;

namespace S6.SnapVault.Models;

/// <summary>
/// Represents a photography event (wedding, conference, birthday, etc.)
/// Entity<T> is correct - this is metadata, not storage-backed media
/// </summary>
public class Event : Entity<Event>
{
    public string Name { get; set; } = "";
    public EventType Type { get; set; }
    public DateTime EventDate { get; set; }
    public string? ClientName { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? GalleryPassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    // Computed properties
    public int PhotoCount { get; set; }
    public StorageTier CurrentTier { get; set; } = StorageTier.Hot;
    public long TotalStorageBytes { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;

    // Storage tier breakdown
    public long HotStorageBytes { get; set; }
    public long WarmStorageBytes { get; set; }
    public long ColdStorageBytes { get; set; }
}

public enum EventType { Wedding, Corporate, Birthday, Graduation, Anniversary, Other }
public enum StorageTier { Hot, Warm, Cold }
public enum ProcessingStatus { Pending, InProgress, Completed, Failed, PartialSuccess }
```

#### PhotoAsset.cs (REQUIRES REVISION)

**Current Implementation Issues**:
1. ❌ Inherits Entity<T> instead of MediaEntity<T>
2. ❌ Uses non-existent [StorageProfile] attribute
3. ❌ Stores URLs directly instead of leveraging MediaEntity storage abstraction

**Architectural Decision Required**: Multi-Tier Storage Approach

We need to store 3 storage variants per photo (thumbnail/gallery/full-res) across different tiers. Since [StorageProfile] doesn't exist at property level, we have **3 options**:

---

## Storage Multi-Tier Architecture Decision

### Option A: Multi-Entity Pattern (Works Today) ⭐ RECOMMENDED

**Approach**: Create 3 separate MediaEntity classes with [StorageBinding] for each storage tier.

**Pros**:
- ✅ Uses framework capabilities (no custom code)
- ✅ Type-safe storage profile binding
- ✅ Automatic storage lifecycle management
- ✅ Framework handles URL generation, presigning
- ✅ Leverages MediaEntity derivation pattern (SourceMediaId, DerivationKey)
- ✅ Aligns with framework philosophy of "Reference = Intent"

**Cons**:
- ⚠️ 3 entities instead of 1 (more data model complexity)
- ⚠️ Requires loading 3 entities to get full photo data
- ⚠️ Relationships via SourceMediaId (navigation not automatic)

**Implementation**:
```csharp
using Koan.Data.Core;
using Koan.Media.Abstractions;
using Koan.Storage.Core;
using Koan.Data.Vector.Core;

namespace S6.SnapVault.Models;

/// <summary>
/// Full-resolution original photo - stored in cold tier
/// This is the source entity; thumbnails/gallery reference this via SourceMediaId
/// </summary>
[StorageBinding(Profile = "cold", Container = "photos-fullres")]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string EventId { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; } // From EXIF

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

    // Derived media references (framework pattern)
    // ThumbnailMediaId and GalleryMediaId point to derived entities
    public string? GalleryMediaId { get; set; } // PhotoGallery.Id
    // ThumbnailMediaId inherited from MediaEntity<T>
}

/// <summary>
/// Gallery-size photo (1200px) - stored in warm tier
/// Derived from PhotoAsset via SourceMediaId
/// </summary>
[StorageBinding(Profile = "warm", Container = "photos-gallery")]
public class PhotoGallery : MediaEntity<PhotoGallery>
{
    // SourceMediaId inherited from MediaEntity - points to PhotoAsset.Id
    // DerivationKey inherited - set to "gallery-1200"
}

/// <summary>
/// Thumbnail (150x150) - stored in hot tier with CDN
/// Derived from PhotoAsset via SourceMediaId
/// </summary>
[StorageBinding(Profile = "hot-cdn", Container = "photos-thumbnails")]
public class PhotoThumbnail : MediaEntity<PhotoThumbnail>
{
    // SourceMediaId inherited from MediaEntity - points to PhotoAsset.Id
    // DerivationKey inherited - set to "thumbnail-150"
}

public class GpsCoordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
}
```

**Usage Pattern**:
```csharp
// Upload full-res photo
var photo = await PhotoAsset.Upload(fileStream, fileName, "image/jpeg");
photo.EventId = eventId;
photo.CapturedAt = exifData.CapturedAt;
await photo.Save();

// Generate gallery-size derivative
var galleryStream = await ResizeImage(photo, 1200);
var gallery = await PhotoGallery.Upload(galleryStream, $"{photo.Id}-gallery.jpg", "image/jpeg");
gallery.SourceMediaId = photo.Id;
gallery.DerivationKey = "gallery-1200";
await gallery.Save();

photo.GalleryMediaId = gallery.Id;

// Generate thumbnail derivative
var thumbnailStream = await ResizeImage(photo, 150);
var thumbnail = await PhotoThumbnail.Upload(thumbnailStream, $"{photo.Id}-thumb.jpg", "image/jpeg");
thumbnail.SourceMediaId = photo.Id;
thumbnail.DerivationKey = "thumbnail-150";
await thumbnail.Save();

photo.ThumbnailMediaId = thumbnail.Id;
await photo.Save();

// Load photo with derivatives
var photo = await PhotoAsset.Get(photoId);
var gallery = await PhotoGallery.Get(photo.GalleryMediaId!);
var thumbnail = await PhotoThumbnail.Get(photo.ThumbnailMediaId!);

// Get URLs
var fullResUrl = await photo.OpenRead(); // or use presigned URL
var galleryUrl = await gallery.OpenRead();
var thumbnailUrl = await thumbnail.OpenRead();
```

---

### Option B: Propose [StorageProfile] Attribute as Framework Feature

**Approach**: Extend framework to support property-level storage profile binding.

**Pros**:
- ✅ Single entity model (simple data model)
- ✅ Natural API for multi-tier storage
- ✅ Framework enhancement benefits all users
- ✅ Aligns with framework's attribute-based discovery

**Cons**:
- ❌ Requires framework changes (blocks sample implementation)
- ❌ Implementation complexity (profile resolution, URL generation)
- ❌ Testing burden (new framework feature)
- ⏰ Delays sample by 1-2 weeks

**Proposed API** (for future framework enhancement):
```csharp
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    [StorageProfile("hot-cdn")]
    public string? ThumbnailKey { get; set; }  // Framework stores key, resolves to URL

    [StorageProfile("warm")]
    public string? GalleryKey { get; set; }

    // FullRes uses entity's default storage binding
}
```

**Framework Work Required**:
1. Create `StorageProfileAttribute : Attribute`
2. Extend MediaEntity<T> to detect property-level attributes
3. Override storage resolution to use property-specific profiles
4. Update IStorageService to handle per-property profile routing
5. Add URL resolution helpers (Key → URL via profile)

**Recommendation**: **NOT FOR THIS SAMPLE** - propose as separate framework enhancement (DX-0047).

---

### Option C: URL Storage (Quick & Dirty)

**Approach**: Store presigned URLs directly as strings, bypass MediaEntity storage abstraction.

**Pros**:
- ✅ Simple implementation (works immediately)
- ✅ Single entity model
- ✅ Direct URL access (no loading derivatives)

**Cons**:
- ❌ Bypasses framework storage abstraction
- ❌ No automatic lifecycle management
- ❌ Presigned URLs expire (need regeneration)
- ❌ Manual tier migration (no framework help)
- ❌ Doesn't demonstrate Koan.Storage patterns (bad for sample)

**Implementation** (NOT RECOMMENDED):
```csharp
public class PhotoAsset : Entity<PhotoAsset>  // Entity, not MediaEntity!
{
    public string ThumbnailUrl { get; set; } = "";
    public string GalleryUrl { get; set; } = "";
    public string FullResUrl { get; set; } = "";
}
```

---

### ✅ ARCHITECTURAL DECISION: Option A (Multi-Entity Pattern)

**Rationale**:
1. **Framework-native**: Uses existing framework capabilities without custom code
2. **Sample purpose**: Demonstrates MediaEntity<T>, [StorageBinding], derivation patterns
3. **Enterprise-ready**: Proper storage lifecycle, automatic tier management
4. **Immediate**: No framework changes required, can implement today

**Trade-offs Accepted**:
- Slightly more complex data model (3 entities vs 1)
- Requires loading derivatives (mitigated by caching, reasonable for sample)

**Implementation Plan**:
- PhotoAsset: Full-res, primary entity with all metadata
- PhotoGallery: Derived gallery-size (1200px)
- PhotoThumbnail: Derived thumbnail (150x150)
- Use SourceMediaId/DerivationKey for relationships
- Services abstract the multi-entity pattern from UI

---

## Service Layer Architecture

### Core Services Implementation

#### 1. IPhotoProcessingService

**Purpose**: Orchestrates photo upload pipeline (EXIF → Resize → AI → Storage)

**Location**: `samples/S6.SnapVault/Services/IPhotoProcessingService.cs`

**Implementation**:
```csharp
using Koan.Media.Abstractions;
using Koan.Media.Core.Operators;
using S6.SnapVault.Models;

namespace S6.SnapVault.Services;

public interface IPhotoProcessingService
{
    Task<PhotoAsset> ProcessUploadAsync(string eventId, IFormFile file, CancellationToken ct = default);
    Task<ProcessingResult> ProcessBatchAsync(string eventId, List<IFormFile> files, IProgress<BatchProgress>? progress = null, CancellationToken ct = default);
}

public class PhotoProcessingService : IPhotoProcessingService
{
    private readonly ILogger<PhotoProcessingService> _logger;
    private readonly IExifExtractionService _exifService;
    private readonly IPhotoVisionService _visionService;
    private readonly ResizeOperator _resizeOperator;
    private readonly RotateOperator _rotateOperator;

    public PhotoProcessingService(
        ILogger<PhotoProcessingService> logger,
        IExifExtractionService exifService,
        IPhotoVisionService visionService,
        ResizeOperator resizeOperator,
        RotateOperator rotateOperator)
    {
        _logger = logger;
        _exifService = exifService;
        _visionService = visionService;
        _resizeOperator = resizeOperator;
        _rotateOperator = rotateOperator;
    }

    public async Task<PhotoAsset> ProcessUploadAsync(string eventId, IFormFile file, CancellationToken ct = default)
    {
        using var stream = file.OpenReadStream();

        // Step 1: Auto-orient using EXIF (framework operator)
        var orientedStream = await _rotateOperator.ExecuteAsync(
            new MemoryStream(await ReadAllBytesAsync(stream)),
            new Dictionary<string, string>(),  // Auto-orient uses EXIF automatically
            ct
        );

        // Step 2: Upload full-res photo (cold tier)
        orientedStream.Position = 0;
        var photo = await PhotoAsset.Upload(orientedStream, file.FileName, file.ContentType ?? "image/jpeg");
        photo.EventId = eventId;
        photo.UploadedAt = DateTime.UtcNow;

        // Step 3: Extract EXIF metadata
        orientedStream.Position = 0;
        var exifData = await _exifService.ExtractAsync(orientedStream, ct);
        photo.CapturedAt = exifData.DateTimeTaken;
        photo.CameraModel = exifData.CameraModel;
        photo.LensModel = exifData.LensModel;
        photo.FocalLength = exifData.FocalLength;
        photo.Aperture = exifData.Aperture;
        photo.ShutterSpeed = exifData.ShutterSpeed;
        photo.ISO = exifData.ISO;
        photo.Location = exifData.Location;

        await photo.Save();

        // Step 4: Generate gallery-size derivative (1200px, warm tier)
        orientedStream.Position = 0;
        var galleryStream = await _resizeOperator.ExecuteAsync(
            orientedStream,
            new Dictionary<string, string>
            {
                ["w"] = "1200",
                ["h"] = "1200",
                ["fit"] = "max",
                ["quality"] = "85"
            },
            ct
        );

        var gallery = await PhotoGallery.Upload(galleryStream, $"{photo.Id}-gallery.jpg", "image/jpeg");
        gallery.SourceMediaId = photo.Id;
        gallery.DerivationKey = "gallery-1200";
        await gallery.Save();

        photo.GalleryMediaId = gallery.Id;

        // Step 5: Generate thumbnail derivative (150x150, hot tier with CDN)
        orientedStream.Position = 0;
        var thumbnailStream = await _resizeOperator.ExecuteAsync(
            orientedStream,
            new Dictionary<string, string>
            {
                ["w"] = "150",
                ["h"] = "150",
                ["fit"] = "crop",  // Square crop for thumbnails
                ["quality"] = "80"
            },
            ct
        );

        var thumbnail = await PhotoThumbnail.Upload(thumbnailStream, $"{photo.Id}-thumb.jpg", "image/jpeg");
        thumbnail.SourceMediaId = photo.Id;
        thumbnail.DerivationKey = "thumbnail-150";
        await thumbnail.Save();

        photo.ThumbnailMediaId = thumbnail.Id;

        // Step 6: AI analysis (async, can fail without blocking)
        try
        {
            orientedStream.Position = 0;
            var analysis = await _visionService.AnalyzePhotoAsync(orientedStream, ct);
            photo.DetectedObjects = analysis.DetectedObjects;
            photo.MoodDescription = analysis.MoodDescription;
            photo.AutoTags = analysis.AutoTags;
            photo.Embedding = analysis.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI analysis failed for photo {PhotoId}, continuing without AI metadata", photo.Id);
        }

        photo.ProcessingStatus = ProcessingStatus.Completed;
        await photo.Save();

        _logger.LogInformation("Processed photo {PhotoId} for event {EventId}", photo.Id, eventId);

        return photo;
    }

    public async Task<ProcessingResult> ProcessBatchAsync(
        string eventId,
        List<IFormFile> files,
        IProgress<BatchProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ProcessingResult { TotalFiles = files.Count };

        for (int i = 0; i < files.Count; i++)
        {
            try
            {
                var photo = await ProcessUploadAsync(eventId, files[i], ct);
                result.ProcessedPhotos.Add(photo);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file {FileName}", files[i].FileName);
                result.Errors.Add($"{files[i].FileName}: {ex.Message}");
                result.FailureCount++;
            }

            progress?.Report(new BatchProgress
            {
                ProcessedCount = i + 1,
                TotalCount = files.Count,
                CurrentFileName = files[i].FileName
            });
        }

        return result;
    }

    private async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}

public class ProcessingResult
{
    public int TotalFiles { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<PhotoAsset> ProcessedPhotos { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class BatchProgress
{
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string CurrentFileName { get; set; } = "";
    public double PercentComplete => TotalCount > 0 ? (ProcessedCount / (double)TotalCount) * 100 : 0;
}
```

---

#### 2. IPhotoVisionService

**Purpose**: Wraps Koan.AI.Vision with domain-specific logic for photo analysis

**Location**: `samples/S6.SnapVault/Services/IPhotoVisionService.cs`

**Implementation**:
```csharp
using Koan.AI.Abstractions;
using Koan.AI.Core;

namespace S6.SnapVault.Services;

public interface IPhotoVisionService
{
    Task<PhotoAnalysis> AnalyzePhotoAsync(Stream imageStream, CancellationToken ct = default);
}

public class PhotoVisionService : IPhotoVisionService
{
    private readonly IAIVisionService _visionService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<PhotoVisionService> _logger;

    public PhotoVisionService(
        IAIVisionService visionService,
        IEmbeddingService embeddingService,
        ILogger<PhotoVisionService> logger)
    {
        _visionService = visionService;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<PhotoAnalysis> AnalyzePhotoAsync(Stream imageStream, CancellationToken ct = default)
    {
        // Use Koan.AI.Vision for image analysis
        var visionResult = await _visionService.AnalyzeAsync(imageStream, new VisionOptions
        {
            DetectObjects = true,
            GenerateDescription = true,
            ExtractText = false
        }, ct);

        // Generate tags from detected objects + mood description
        var autoTags = GenerateAutoTags(visionResult.Objects, visionResult.Description);

        // Generate embedding for semantic search
        var embeddingText = $"{visionResult.Description} {string.Join(" ", autoTags)}";
        var embedding = await _embeddingService.GenerateAsync(embeddingText, ct);

        return new PhotoAnalysis
        {
            DetectedObjects = visionResult.Objects,
            MoodDescription = visionResult.Description,
            AutoTags = autoTags,
            Embedding = embedding
        };
    }

    private List<string> GenerateAutoTags(List<string> objects, string description)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add detected objects
        foreach (var obj in objects.Take(5))
        {
            tags.Add(obj.ToLowerInvariant());
        }

        // Extract mood keywords from description
        var moodKeywords = ExtractMoodKeywords(description);
        foreach (var keyword in moodKeywords)
        {
            tags.Add(keyword);
        }

        return tags.Take(10).ToList();
    }

    private List<string> ExtractMoodKeywords(string description)
    {
        // Simple keyword extraction (can be enhanced with NLP)
        var keywords = new List<string>();
        var moodWords = new[] { "romantic", "formal", "casual", "outdoor", "indoor", "sunset", "daytime", "evening", "celebration", "ceremony" };

        foreach (var word in moodWords)
        {
            if (description.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                keywords.Add(word);
            }
        }

        return keywords;
    }
}

public class PhotoAnalysis
{
    public List<string> DetectedObjects { get; set; } = new();
    public string MoodDescription { get; set; } = "";
    public List<string> AutoTags { get; set; } = new();
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
```

---

#### 3. IPhotoStorage Service

**Purpose**: Abstracts multi-entity storage pattern, provides convenience methods for URL access and tier migration

**Location**: `samples/S6.SnapVault/Services/IPhotoStorage.cs`

**Implementation**:
```csharp
using Koan.Storage.Abstractions;
using S6.SnapVault.Models;

namespace S6.SnapVault.Services;

public interface IPhotoStorage
{
    Task<PhotoUrls> GetPhotoUrlsAsync(string photoId, TimeSpan urlExpiration = default, CancellationToken ct = default);
    Task MigrateToTierAsync(string photoId, StorageTier targetTier, CancellationToken ct = default);
}

public class PhotoStorage : IPhotoStorage
{
    private readonly IStorageService _storageService;
    private readonly ILogger<PhotoStorage> _logger;

    public PhotoStorage(IStorageService storageService, ILogger<PhotoStorage> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<PhotoUrls> GetPhotoUrlsAsync(string photoId, TimeSpan urlExpiration = default, CancellationToken ct = default)
    {
        var expiration = urlExpiration == default ? TimeSpan.FromHours(1) : urlExpiration;

        // Load photo and derivatives
        var photo = await PhotoAsset.Get(photoId, ct);
        if (photo == null)
            throw new InvalidOperationException($"Photo {photoId} not found");

        // Get presigned URLs for each storage tier
        var thumbnailUrl = photo.ThumbnailMediaId != null
            ? await GetPresignedUrlAsync("hot-cdn", "photos-thumbnails", photo.ThumbnailMediaId, expiration, ct)
            : null;

        var galleryUrl = photo.GalleryMediaId != null
            ? await GetPresignedUrlAsync("warm", "photos-gallery", photo.GalleryMediaId, expiration, ct)
            : null;

        var fullResUrl = await GetPresignedUrlAsync("cold", "photos-fullres", photo.Id, expiration, ct);

        return new PhotoUrls
        {
            ThumbnailUrl = thumbnailUrl,
            GalleryUrl = galleryUrl,
            FullResUrl = fullResUrl
        };
    }

    public async Task MigrateToTierAsync(string photoId, StorageTier targetTier, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(photoId, ct);
        if (photo == null)
            throw new InvalidOperationException($"Photo {photoId} not found");

        // Migrate gallery and full-res (thumbnails always stay hot)
        var targetProfile = GetStorageProfile(targetTier);

        // Migrate full-res
        await _storageService.TransferToProfileAsync(
            sourceProfile: "cold",
            sourceContainer: "photos-fullres",
            key: photo.Key,
            targetProfile: targetProfile,
            ct: ct
        );

        // Migrate gallery
        if (photo.GalleryMediaId != null)
        {
            var gallery = await PhotoGallery.Get(photo.GalleryMediaId, ct);
            if (gallery != null)
            {
                await _storageService.TransferToProfileAsync(
                    sourceProfile: "warm",
                    sourceContainer: "photos-gallery",
                    key: gallery.Key,
                    targetProfile: targetProfile,
                    ct: ct
                );
            }
        }

        _logger.LogInformation("Migrated photo {PhotoId} to {Tier} tier", photoId, targetTier);
    }

    private async Task<string?> GetPresignedUrlAsync(string profile, string container, string key, TimeSpan expiration, CancellationToken ct)
    {
        try
        {
            var uri = await _storageService.PresignReadAsync(profile, container, key, expiration, ct);
            return uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate presigned URL for {Profile}/{Container}/{Key}", profile, container, key);
            return null;
        }
    }

    private string GetStorageProfile(StorageTier tier) => tier switch
    {
        StorageTier.Hot => "hot-cdn",
        StorageTier.Warm => "warm",
        StorageTier.Cold => "cold",
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };
}

public class PhotoUrls
{
    public string? ThumbnailUrl { get; set; }
    public string? GalleryUrl { get; set; }
    public string? FullResUrl { get; set; }
}
```

---

#### 4. Backup Integration (IBackupService)

**Purpose**: Replace custom BackupJob entity with framework's IBackupService

**Location**: `samples/S6.SnapVault/Services/EventBackupService.cs`

**Implementation**:
```csharp
using Koan.Data.Backup.Abstractions;
using S6.SnapVault.Models;

namespace S6.SnapVault.Services;

public interface IEventBackupService
{
    Task<string> BackupEventAsync(string eventId, CancellationToken ct = default);
    Task<RestoreResult> RestoreEventAsync(string backupName, string? targetEventId = null, CancellationToken ct = default);
    Task<BackupProgress> GetBackupProgressAsync(string backupId, CancellationToken ct = default);
}

public class EventBackupService : IEventBackupService
{
    private readonly IBackupService _backupService;
    private readonly ILogger<EventBackupService> _logger;

    public EventBackupService(IBackupService backupService, ILogger<EventBackupService> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<string> BackupEventAsync(string eventId, CancellationToken ct = default)
    {
        var evt = await Event.Get(eventId, ct);
        if (evt == null)
            throw new InvalidOperationException($"Event {eventId} not found");

        var backupName = $"event-{eventId}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        _logger.LogInformation("Starting backup for event {EventId} as {BackupName}", eventId, backupName);

        // Use framework's IBackupService to backup PhotoAsset entities
        var manifest = await _backupService.BackupEntityAsync<PhotoAsset, string>(
            backupName: backupName,
            options: new BackupOptions
            {
                Filter = photo => photo.EventId == eventId,
                IncludeRelatedEntities = true,  // Include gallery/thumbnail derivatives
                Compression = true,
                Encryption = true
            },
            ct: ct
        );

        _logger.LogInformation("Backup {BackupName} completed: {PhotoCount} photos, {SizeBytes} bytes",
            backupName, manifest.EntityCount, manifest.TotalSizeBytes);

        return manifest.BackupId;
    }

    public async Task<RestoreResult> RestoreEventAsync(string backupName, string? targetEventId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting restore from backup {BackupName}", backupName);

        // Use framework's IBackupService to restore entities
        var result = await _backupService.RestoreEntityAsync<PhotoAsset, string>(
            backupName: backupName,
            options: new RestoreOptions
            {
                OverwriteExisting = targetEventId == null,  // Overwrite if restoring to original event
                Transform = targetEventId != null
                    ? (photo => { photo.EventId = targetEventId; return photo; })  // Redirect to new event
                    : null
            },
            ct: ct
        );

        _logger.LogInformation("Restore from {BackupName} completed: {PhotoCount} photos restored",
            backupName, result.RestoredCount);

        return new RestoreResult
        {
            RestoredCount = result.RestoredCount,
            FailedCount = result.FailedCount,
            Errors = result.Errors
        };
    }

    public async Task<BackupProgress> GetBackupProgressAsync(string backupId, CancellationToken ct = default)
    {
        // Framework IBackupService provides progress tracking
        return await _backupService.GetBackupProgressAsync(backupId, ct);
    }
}

public class RestoreResult
{
    public int RestoredCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

---

## Implementation Roadmap with Framework Mapping

### Phase 1: Core Infrastructure (Week 1-2) - REVISED

**Framework Capabilities**:
- ✅ MediaEntity<T> for photo storage
- ✅ [StorageBinding] for tier configuration
- ✅ MediaOperators for image processing
- ✅ Entity<T> for event/job tracking

**Tasks**:
1. ✅ Create entity models:
   - Event.cs (no changes required)
   - PhotoAsset.cs (revise to MediaEntity)
   - PhotoGallery.cs (new - gallery derivative)
   - PhotoThumbnail.cs (new - thumbnail derivative)
   - ProcessingJob.cs (no changes required)
   - **REMOVE BackupJob.cs** (use IBackupService)

2. ✅ Implement services:
   - IPhotoProcessingService (uses MediaOperators)
   - IExifExtractionService (MetadataExtractor library)
   - IPhotoVisionService (wraps Koan.AI.Vision)
   - IPhotoStorage (abstracts multi-entity pattern)
   - IEventBackupService (wraps IBackupService)

3. ✅ Configure storage profiles in appsettings.json:
```json
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "hot-cdn": {
          "Provider": "filesystem",
          "Container": "photos-thumbnails",
          "BaseUrl": "http://localhost:5094/storage/hot",
          "Options": {
            "BasePath": "./storage/hot",
            "CdnEnabled": true
          }
        },
        "warm": {
          "Provider": "filesystem",
          "Container": "photos-gallery",
          "Options": {
            "BasePath": "./storage/warm"
          }
        },
        "cold": {
          "Provider": "filesystem",
          "Container": "photos-fullres",
          "Options": {
            "BasePath": "./storage/cold"
          }
        }
      }
    },
    "AI": {
      "Vision": {
        "Provider": "openai",
        "Model": "gpt-4-vision-preview"
      }
    },
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

4. ✅ Implement API controllers:
   - EventsController.cs (inherits EntityController<Event>)
   - PhotosController.cs (inherits EntityController<PhotoAsset>)
   - ProcessingController.cs (job status endpoints)

**Timeline**: 1.5 weeks (reduced from 2 weeks due to framework usage)

---

### Phase 2: Grid Gallery Mode (Week 3)

**Framework Capabilities**:
- ✅ MediaEntity URLs via IPhotoStorage
- Entity<T> queries for pagination

**Tasks**:
1. Build virtual scrolling grid
2. Lazy loading with Intersection Observer
3. Lightbox viewer component
4. Favorite marking functionality
5. Download full-res functionality

**Timeline**: 1 week (unchanged)

---

### Phase 3: AI Features (Week 4)

**Framework Capabilities**:
- ✅ Koan.AI.Vision for image analysis
- ✅ [Vector] attribute for semantic search
- ✅ Koan.Data.Vector for search queries

**Tasks**:
1. Integrate IPhotoVisionService in upload pipeline
2. Implement semantic search endpoint:
```csharp
[HttpPost("api/photos/search")]
public async Task<ActionResult<SearchResponse>> SearchPhotos([FromBody] SearchRequest request)
{
    var photos = await PhotoAsset.SemanticSearch(
        query: request.Query,
        topK: request.Limit,
        filter: p => string.IsNullOrEmpty(request.EventId) || p.EventId == request.EventId
    );

    // Load URLs for each photo
    var photosWithUrls = new List<PhotoResult>();
    foreach (var photo in photos)
    {
        var urls = await _photoStorage.GetPhotoUrlsAsync(photo.Id);
        photosWithUrls.Add(new PhotoResult
        {
            Photo = photo,
            Urls = urls
        });
    }

    return Ok(new SearchResponse
    {
        Photos = photosWithUrls,
        Query = request.Query,
        ResultCount = photos.Count
    });
}
```
3. Build search UI with debounce
4. Display AI metadata (tags, mood description)

**Timeline**: 0.75 weeks (reduced from 1 week due to framework usage)

---

### Phase 4: Event Timeline Mode (Week 5)

**Framework Capabilities**:
- ✅ Entity<T> LINQ queries for grouping
- SignalR for real-time updates

**Tasks**:
1. Timeline grouping by month/year
2. Event cards with metrics
3. Storage tier visualization
4. Manual archive functionality using IPhotoStorage.MigrateToTierAsync()
5. Real-time processing status via SignalR

**Timeline**: 1 week (unchanged)

---

### Phase 5: Backup & Dashboard (Week 6)

**Framework Capabilities**:
- ✅ IBackupService for backup/restore
- ✅ BackupProgress for progress tracking

**Tasks**:
1. Implement backup endpoints using IEventBackupService
2. Build backup browser UI
3. Implement restore functionality
4. Dashboard metrics (storage, events, activity)
5. Scheduled backup job (Koan.Scheduling)

**Timeline**: 0.75 weeks (reduced from 1 week due to IBackupService)

---

### Phase 6: Polish & Documentation (Week 6.5-7)

**Tasks**:
1. Performance optimization pass
2. Comprehensive README (S5.Recs template)
3. Testing examples
4. Docker Compose setup
5. Demo video/screenshots

**Timeline**: 0.5-1 week

---

## Total Timeline: 5-6 weeks (reduced from 6.5 weeks)

**Time savings from framework usage**:
- Phase 1: -0.5 weeks (MediaEntity, MediaOperators, IBackupService)
- Phase 3: -0.25 weeks (AI.Vision integration)
- Phase 5: -0.25 weeks (IBackupService vs custom implementation)

---

## Framework Capabilities Checklist

Use this checklist to ensure comprehensive framework demonstration:

### Koan.Data
- [ ] Entity<T> for metadata entities (Event, ProcessingJob)
- [ ] MediaEntity<T> for storage-backed media (PhotoAsset, PhotoGallery, PhotoThumbnail)
- [ ] [StorageBinding] for entity-level storage profiles
- [ ] Entity<T>.Get(), Save(), Query(), All() patterns
- [ ] LINQ query pushdown capabilities

### Koan.Storage
- [ ] IStorageService.TransferToProfileAsync() for tier migration
- [ ] IStorageService.PresignReadAsync() for URL generation
- [ ] Multi-profile storage (hot-cdn, warm, cold)
- [ ] Automatic storage lifecycle management

### Koan.Media
- [ ] MediaEntity<T> upload/download patterns
- [ ] MediaOperators: ResizeOperator, RotateOperator
- [ ] EXIF auto-orientation
- [ ] Derivation relationships (SourceMediaId, DerivationKey)

### Koan.Data.Backup
- [ ] IBackupService.BackupEntityAsync<T,K>()
- [ ] IBackupService.RestoreEntityAsync<T,K>()
- [ ] BackupProgress tracking
- [ ] Backup filtering, compression, encryption

### Koan.AI
- [ ] IAIVisionService for image analysis
- [ ] IEmbeddingService for vector generation
- [ ] Provider-agnostic AI integration

### Koan.Data.Vector
- [ ] [Vector] attribute for semantic search
- [ ] SemanticSearch() static API
- [ ] Query filters with semantic search

### Koan.Web
- [ ] EntityController<T> for auto-CRUD
- [ ] Custom controller actions
- [ ] SignalR for real-time updates

### Koan.Scheduling
- [ ] Scheduled jobs for tier aging
- [ ] Scheduled backups
- [ ] Cron expressions

---

## Testing Strategy

### Unit Tests
1. **PhotoProcessingService**: Test EXIF extraction, resize operations, AI integration
2. **PhotoStorage**: Test URL generation, tier migration
3. **EventBackupService**: Test backup/restore workflows

### Integration Tests
1. **Upload Pipeline**: End-to-end photo upload with all processing steps
2. **Semantic Search**: Verify vector storage and search accuracy
3. **Tier Migration**: Verify storage tier transitions
4. **Backup/Restore**: Verify data integrity after restore

### Performance Tests
1. **Virtual Scrolling**: 500 photos in grid, measure FPS
2. **Batch Upload**: 50 photos, measure throughput
3. **Semantic Search**: Measure latency with 1000+ photos
4. **Backup**: Measure backup speed for 100 photos

---

## Next Steps for Implementation

**Ready to implement**: Use this command:
```
Continue with implementation of S6.SnapVault, documented in DX-0046-IMPLEMENTATION.md
```

**Implementation order**:
1. **Revise entity models** (PhotoAsset → MediaEntity, remove BackupJob)
2. **Implement services** (PhotoProcessing, PhotoVision, PhotoStorage, EventBackup)
3. **Update controllers** (use services, return URLs)
4. **Build UI** (Grid Gallery → AI Search → Timeline → Backup)
5. **Docker Compose** (MongoDB, Qdrant, OpenAI config)
6. **README** (comprehensive guide following S5.Recs template)

---

## References

- **Parent ADR**: [DX-0046](./DX-0046-s6-snapvault-feature-specification.md)
- **Framework Research**: Explore task findings (MediaEntity, IBackupService, MediaOperators)
- **Reference Sample**: S16.PantryPal (IPhotoStorage, IPhotoVisionService patterns)
- **Framework Docs**: `docs/capability-map.md`, `docs/module-ledger.md`

---

**Status**: Ready for implementation
**Last Updated**: 2025-10-16
**Framework Version**: v0.6.3
