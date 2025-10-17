using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using S6.SnapVault.Models;
using Koan.Media.Core.Extensions;
using Koan.AI;
using Koan.Data.Core;
using Koan.Data.Vector;

namespace S6.SnapVault.Services;

/// <summary>
/// Photo processing service using DX-0047 Fluent Media Transform API
/// </summary>
internal sealed class PhotoProcessingService : IPhotoProcessingService
{
    private readonly ILogger<PhotoProcessingService> _logger;

    public PhotoProcessingService(ILogger<PhotoProcessingService> logger)
    {
        _logger = logger;
    }

    public async Task<PhotoAsset> ProcessUploadAsync(string? eventId, IFormFile file, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing upload: {FileName} for event {EventId}", file.FileName, eventId ?? "auto");

        // Create PhotoAsset entity (eventId will be set after EXIF extraction if null)
        var photo = new PhotoAsset
        {
            EventId = eventId ?? "", // Temporary, will be set after determining date
            OriginalFileName = file.FileName,
            UploadedAt = DateTime.UtcNow,
            ProcessingStatus = ProcessingStatus.InProgress
        };

        try
        {
            // Open source stream
            using var sourceStream = file.OpenReadStream();

            // Extract dimensions before transformations
            using (var dimensionCheck = new MemoryStream())
            {
                await sourceStream.CopyToAsync(dimensionCheck, ct);
                dimensionCheck.Position = 0;
                sourceStream.Position = 0;

                var info = await Image.IdentifyAsync(dimensionCheck, ct);
                photo.Width = info.Width;
                photo.Height = info.Height;
            }

            // Extract EXIF metadata (including capture date)
            await ExtractExifMetadataAsync(photo, sourceStream, ct);
            sourceStream.Position = 0;

            // If no eventId provided, auto-create or get daily event based on capture date
            if (string.IsNullOrEmpty(eventId))
            {
                var eventDate = photo.CapturedAt ?? photo.UploadedAt;
                var dailyEvent = await GetOrCreateDailyEventAsync(eventDate, ct);
                photo.EventId = dailyEvent.Id;
                _logger.LogInformation("Auto-assigned photo to daily event: {EventName}", dailyEvent.Name);
            }

            // Upload full-resolution to cold storage using MediaEntity<T> pattern
            var fullResEntity = await PhotoAsset.Upload(sourceStream, file.FileName, file.ContentType, ct: ct);
            photo.Id = fullResEntity.Id;
            photo.Key = fullResEntity.Key;
            photo.ContentType = fullResEntity.ContentType;
            photo.Size = fullResEntity.Size;

            // Use DX-0047 fluent API to create derivatives
            // Branch 1: Gallery view (1200px max)
            var galleryStream = await fullResEntity.OpenRead(ct);
            var autoOriented = await galleryStream.AutoOrient(ct);
            var resized = await autoOriented.ResizeFit(1200, 1200, ct);
            var galleryResult = await resized.Result(ct);

            var galleryBranch = galleryResult.Branch();
            var galleryDimensions = await GetStreamDimensions(galleryBranch, ct);
            var galleryEntity = await PhotoGallery.Upload(galleryBranch, $"{photo.Id}_gallery.jpg", "image/jpeg", ct: ct);
            await galleryEntity.Save(ct); // Save gallery entity to database

            photo.GalleryMediaId = galleryEntity.Id;

            // Branch 2: Thumbnail (150x150 square crop for grid views)
            var thumbnailBranch = galleryResult.Branch();
            var cropped = await thumbnailBranch.CropSquare(ct: ct);
            var thumbnailStream = await cropped.ResizeFit(150, 150, ct);
            var thumbnailEntity = await PhotoThumbnail.Upload(thumbnailStream, $"{photo.Id}_thumb.jpg", "image/jpeg", ct: ct);
            await thumbnailEntity.Save(ct); // Save thumbnail entity to database

            // Set base MediaEntity ThumbnailMediaId (protected internal setter, but PhotoAsset is derived)
            typeof(PhotoAsset).BaseType!.GetProperty("ThumbnailMediaId")!.SetValue(photo, thumbnailEntity.Id);

            // Branch 3: Masonry thumbnail (300px max, preserve aspect ratio for masonry layouts)
            var masonryBranch = galleryResult.Branch();
            var masonryResized = await masonryBranch.ResizeFit(300, 300, ct);
            var masonryEntity = await PhotoMasonryThumbnail.Upload(masonryResized, $"{photo.Id}_masonry.jpg", "image/jpeg", ct: ct);
            await masonryEntity.Save(ct); // Save masonry thumbnail entity to database

            photo.MasonryThumbnailMediaId = masonryEntity.Id;

            await galleryResult.DisposeAsync();

            // Save photo entity (without AI metadata yet)
            await photo.Save(ct);

            _logger.LogInformation(
                "Photo processed: {PhotoId} ({Width}x{Height}) -> Gallery: {GalleryId}, Thumbnail: {ThumbId}, Masonry: {MasonryId}",
                photo.Id, photo.Width, photo.Height, photo.GalleryMediaId, photo.ThumbnailMediaId, photo.MasonryThumbnailMediaId);

            // Generate AI metadata asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await GenerateAIMetadataAsync(photo, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate AI metadata for photo {PhotoId}", photo.Id);
                }
            }, CancellationToken.None);

            photo.ProcessingStatus = ProcessingStatus.Completed;
            await photo.Save(ct);

            return photo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process photo {FileName}", file.FileName);
            photo.ProcessingStatus = ProcessingStatus.Failed;
            throw;
        }
    }

    public async Task<PhotoAsset> GenerateAIMetadataAsync(PhotoAsset photo, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Generating AI metadata for photo {PhotoId}", photo.Id);

            // Generate detailed description using vision AI
            await GenerateDetailedDescriptionAsync(photo, ct);

            // Build embedding text from available metadata (including detailed description)
            var embeddingText = BuildEmbeddingText(photo);

            // Generate embedding using Koan AI (S5.Recs pattern)
            var embedding = await Koan.AI.Ai.Embed(embeddingText, ct);

            // Prepare vector metadata for hybrid search
            var vectorMetadata = new Dictionary<string, object>
            {
                ["originalFileName"] = photo.OriginalFileName,
                ["eventId"] = photo.EventId,
                ["searchText"] = embeddingText // Required for hybrid search
            };

            // Save with vector using framework pattern (S5.Recs line 740)
            await Data<PhotoAsset, string>.SaveWithVector(photo, embedding, vectorMetadata, ct);

            photo.ProcessingStatus = ProcessingStatus.Completed;
            _logger.LogInformation("AI metadata generated for photo {PhotoId}", photo.Id);

            return photo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI metadata for photo {PhotoId}", photo.Id);
            photo.ProcessingStatus = ProcessingStatus.Failed;
            await photo.Save(ct);
            throw;
        }
    }

    public async Task<List<PhotoAsset>> SemanticSearchAsync(string query, string? eventId = null, double alpha = 0.5, int topK = 20, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Semantic search: query='{Query}' alpha={Alpha} eventId={EventId} topK={TopK}", query, alpha, eventId, topK);

            // Generate query embedding
            var queryVector = await Koan.AI.Ai.Embed(query, ct);

            // Check if vector search is available
            if (!Vector<PhotoAsset>.IsAvailable)
            {
                _logger.LogWarning("Vector search unavailable, falling back to keyword search");
                return await FallbackKeywordSearch(query, eventId, topK, ct);
            }

            // Perform hybrid vector search with user-controlled alpha (S5.Recs pattern - line 140)
            var vectorResults = await Vector<PhotoAsset>.Search(
                vector: queryVector,
                text: query,  // Enables hybrid search
                alpha: alpha,   // User-controlled: 0.0 = exact, 1.0 = semantic
                topK: topK,
                ct: ct
            );

            // Load photo entities
            var photos = new List<PhotoAsset>();
            foreach (var match in vectorResults.Matches)
            {
                var photo = await PhotoAsset.Get(match.Id, ct);
                if (photo != null)
                {
                    // Filter by event if specified
                    if (string.IsNullOrEmpty(eventId) || photo.EventId == eventId)
                    {
                        photos.Add(photo);
                    }
                }
            }

            _logger.LogInformation("Semantic search returned {Count} results", photos.Count);
            return photos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed, using fallback");
            return await FallbackKeywordSearch(query, eventId, topK, ct);
        }
    }

    private async Task<List<PhotoAsset>> FallbackKeywordSearch(string query, string? eventId, int topK, CancellationToken ct)
    {
        var photos = await PhotoAsset.Query(p =>
            (string.IsNullOrEmpty(eventId) || p.EventId == eventId) &&
            (p.OriginalFileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             p.AutoTags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             p.MoodDescription.Contains(query, StringComparison.OrdinalIgnoreCase)), ct);

        return photos.Take(topK).ToList();
    }

    private async Task ExtractExifMetadataAsync(PhotoAsset photo, Stream stream, CancellationToken ct)
    {
        try
        {
            stream.Position = 0;
            using var image = await Image.LoadAsync(stream, ct);

            var exif = image.Metadata.ExifProfile;
            if (exif == null) return;

            // Camera info
            if (exif.TryGetValue(ExifTag.Model, out var modelValue))
                photo.CameraModel = modelValue.Value?.ToString();

            if (exif.TryGetValue(ExifTag.LensModel, out var lensValue))
                photo.LensModel = lensValue.Value?.ToString();

            // Capture settings
            if (exif.TryGetValue(ExifTag.FocalLength, out var focalLengthValue))
                photo.FocalLength = $"{focalLengthValue.Value}mm";

            if (exif.TryGetValue(ExifTag.FNumber, out var apertureValue))
                photo.Aperture = $"f/{apertureValue.Value}";

            if (exif.TryGetValue(ExifTag.ExposureTime, out var shutterValue))
                photo.ShutterSpeed = shutterValue.Value.ToString();

            if (exif.TryGetValue(ExifTag.ISOSpeedRatings, out var isoValue) && isoValue.Value is ushort[] isoArray && isoArray.Length > 0)
                photo.ISO = isoArray[0];

            // Capture date
            if (exif.TryGetValue(ExifTag.DateTimeOriginal, out var dateValue) &&
                dateValue.Value != null &&
                DateTime.TryParse(dateValue.Value.ToString(), out var capturedAt))
            {
                photo.CapturedAt = DateTime.SpecifyKind(capturedAt, DateTimeKind.Utc);
            }

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

                if (exif.TryGetValue(ExifTag.GPSAltitude, out var altValue))
                    photo.Location.Altitude = Convert.ToDouble(altValue.Value);
            }

            _logger.LogDebug("EXIF extracted: Camera={Camera} ISO={ISO} Date={Date}",
                photo.CameraModel, photo.ISO, photo.CapturedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract EXIF metadata");
        }
    }

    private static double ConvertToDecimalDegrees(Rational[] coordinate)
    {
        if (coordinate == null || coordinate.Length != 3)
            return 0;

        var degrees = coordinate[0].ToDouble();
        var minutes = coordinate[1].ToDouble();
        var seconds = coordinate[2].ToDouble();

        return degrees + (minutes / 60.0) + (seconds / 3600.0);
    }

    private static string BuildEmbeddingText(PhotoAsset photo)
    {
        var parts = new List<string>();

        // Detailed AI description first (most comprehensive)
        if (!string.IsNullOrEmpty(photo.DetailedDescription))
            parts.Add(photo.DetailedDescription);

        if (!string.IsNullOrEmpty(photo.OriginalFileName))
            parts.Add($"Filename: {photo.OriginalFileName}");

        if (photo.AutoTags.Any())
            parts.Add($"Tags: {string.Join(", ", photo.AutoTags)}");

        if (!string.IsNullOrEmpty(photo.MoodDescription))
            parts.Add($"Mood: {photo.MoodDescription}");

        if (photo.DetectedObjects.Any())
            parts.Add($"Objects: {string.Join(", ", photo.DetectedObjects)}");

        if (!string.IsNullOrEmpty(photo.CameraModel))
            parts.Add($"Camera: {photo.CameraModel}");

        if (photo.Location != null)
            parts.Add($"Location: {photo.Location.Latitude}, {photo.Location.Longitude}");

        return string.Join("\n", parts);
    }

    private static async Task<(int width, int height)> GetStreamDimensions(Stream stream, CancellationToken ct)
    {
        var originalPosition = stream.Position;
        stream.Position = 0;
        var info = await Image.IdentifyAsync(stream, ct);
        stream.Position = originalPosition;
        return (info.Width, info.Height);
    }

    /// <summary>
    /// Generate detailed AI description for a photo using vision model
    /// </summary>
    private async Task GenerateDetailedDescriptionAsync(PhotoAsset photo, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Generating detailed AI description for photo {PhotoId}", photo.Id);

            // Load image bytes from storage
            var imageStream = await photo.OpenRead(ct);
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, ct);
            var imageBytes = ms.ToArray();

            // Detailed vision analysis prompt
            var prompt = @"Role: You are a meticulous visual analyst. Describe what is visibly present in the image. Do not guess brand/model names, locations, or emotions beyond what is supported by the visuals.

Output format: Markdown with the sections below. Keep it concise but specific. If a detail is unclear, write ""unclear"" or ""unreadable"" rather than guessing.

1) Subject

Who/what is the main subject? (human/character/object/animal)

Visible attributes: age range, build/shape, notable features (hair, ears, markings), pose/gesture, facial expression (if readable).

2) Wardrobe / Props / Key Elements

Garments or objects from top → bottom (or front → back for products).

For each, note: type, silhouette/cut, material/finish (matte/leather/satin/metal), color/undertone, hardware/trim (buckles, zippers, chains), patterns/graphics, wear/texture.

3) Environment & Composition

Setting (studio/backdrop/interior/exterior/scenic), background elements, set dressing.

Framing (full body, half, close-up), angle (eye/low/high), camera distance, rule-of-thirds/centered, negative space.

Depth of field (shallow/deep), bokeh or motion blur if present.

4) Lighting & Color

Light sources (softbox, spotlight, window, daylight), direction (front/side/back/rim), quality (soft/hard), contrast ratio if clear.

Color temperature/grade (cool/warm/neutral), dominant palette, reflections/specular highlights.

5) Technical/Surface Details

Visible textures (gloss, grain, knit, suede, metal polish).

Any legible text/symbols: transcribe exactly; if not readable, write ""unreadable"".

Artifacts (film grain, compression), post effects (vignette, glow, color shift).

6) Safety & Grounding

Avoid private info or identity claims.

No brand/location/model IDs unless explicitly visible and legible.

7) One-Sentence Alt Text

1–2 sentences, 140–200 characters, purely descriptive.

8) Tags (8–15)

Short, searchable keywords: subject, materials, colors, setting, mood, shot type (e.g., ""black leather, thigh-high boots, studio spotlight, dramatic rim light, cyberpunk, full-body portrait"").";

            // Use vision model (qwen2.5-vl:7b) with explicit options
            var visionOptions = new Koan.AI.Contracts.Options.AiVisionOptions
            {
                ImageBytes = imageBytes,
                Prompt = prompt,
                Model = "qwen2.5-vl:7b",
                Temperature = 0.7
            };

            var description = await Koan.AI.Ai.Understand(visionOptions, ct);

            photo.DetailedDescription = description;
            await photo.Save(ct);

            _logger.LogInformation("Detailed AI description generated for photo {PhotoId} ({Length} chars)",
                photo.Id, description.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate detailed description for photo {PhotoId}, continuing without it", photo.Id);
            // Non-fatal - continue processing even if vision description fails
        }
    }

    /// <summary>
    /// Get or create a daily event for the given date
    /// Event name format: "October 1, 2025"
    /// </summary>
    private async Task<Event> GetOrCreateDailyEventAsync(DateTime date, CancellationToken ct)
    {
        // Normalize to date only (UTC)
        var normalizedDate = date.Date;

        // Generate event name in format: "October 1, 2025"
        var eventName = normalizedDate.ToString("MMMM d, yyyy");

        // Check if event already exists for this date
        var existingEvents = await Event.Query(e =>
            e.Type == EventType.DailyAuto &&
            e.EventDate.Date == normalizedDate,
            ct);

        if (existingEvents.Any())
        {
            return existingEvents.First();
        }

        // Create new daily event
        var newEvent = new Event
        {
            Name = eventName,
            Type = EventType.DailyAuto,
            EventDate = normalizedDate,
            Description = $"Auto-generated album for photos taken on {eventName}",
            CreatedAt = DateTime.UtcNow,
            ProcessingStatus = ProcessingStatus.InProgress
        };

        await newEvent.Save(ct);

        _logger.LogInformation("Created daily event: {EventName} ({EventId})", eventName, newEvent.Id);

        return newEvent;
    }
}
