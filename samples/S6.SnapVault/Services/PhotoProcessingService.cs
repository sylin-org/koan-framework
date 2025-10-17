using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using S6.SnapVault.Models;
using S6.SnapVault.Hubs;
using Koan.Media.Core.Extensions;
using Koan.AI;
using Koan.Data.Core;
using Koan.Data.Vector;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace S6.SnapVault.Services;

/// <summary>
/// Photo processing service using DX-0047 Fluent Media Transform API
/// </summary>
internal sealed class PhotoProcessingService : IPhotoProcessingService
{
    private readonly ILogger<PhotoProcessingService> _logger;
    private readonly IHubContext<PhotoProcessingHub> _hubContext;

    public PhotoProcessingService(
        ILogger<PhotoProcessingService> logger,
        IHubContext<PhotoProcessingHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<PhotoAsset> ProcessUploadAsync(string? eventId, IFormFile file, string jobId, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing upload: {FileName} for event {EventId} (job: {JobId})", file.FileName, eventId ?? "auto", jobId);

        // Create PhotoAsset entity (eventId will be set after EXIF extraction if null)
        var photo = new PhotoAsset
        {
            EventId = eventId ?? "", // Temporary, will be set after determining date
            OriginalFileName = file.FileName,
            UploadedAt = DateTime.UtcNow,
            ProcessingStatus = ProcessingStatus.InProgress
        };

        // Helper method for emitting progress events
        async Task EmitProgressAsync(string photoId, string status, string stage, string? error = null)
        {
            try
            {
                await _hubContext.Clients.Group($"job:{jobId}").SendAsync("PhotoProgress", new PhotoProgressEvent
                {
                    JobId = jobId,
                    PhotoId = photoId,
                    FileName = file.FileName,
                    Status = status,
                    Stage = stage,
                    Error = error
                }, CancellationToken.None); // Don't propagate CT to SignalR
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to emit SignalR progress event for {PhotoId}", photoId);
            }
        }

        try
        {
            await EmitProgressAsync("", "processing", "upload");

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

            await EmitProgressAsync("", "processing", "exif");

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

            await EmitProgressAsync(photo.Id, "processing", "thumbnails");

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

            // Branch 4: Retina thumbnail (600px max, preserve aspect ratio for retina/4K displays)
            var retinaBranch = galleryResult.Branch();
            var retinaResized = await retinaBranch.ResizeFit(600, 600, ct);
            var retinaEntity = await PhotoRetinaThumbnail.Upload(retinaResized, $"{photo.Id}_retina.jpg", "image/jpeg", ct: ct);
            await retinaEntity.Save(ct); // Save retina thumbnail entity to database

            photo.RetinaThumbnailMediaId = retinaEntity.Id;

            await galleryResult.DisposeAsync();

            // Save photo entity (without AI metadata yet)
            await photo.Save(ct);

            // Update job progress
            await UpdateJobProgressAsync(jobId, ct);

            // Update event photo count
            await UpdateEventPhotoCountAsync(photo.EventId, ct);

            _logger.LogInformation(
                "Photo processed: {PhotoId} ({Width}x{Height}) -> Gallery: {GalleryId}, Retina: {RetinaId}, Masonry: {MasonryId}, Thumbnail: {ThumbId}",
                photo.Id, photo.Width, photo.Height, photo.GalleryMediaId, photo.RetinaThumbnailMediaId, photo.MasonryThumbnailMediaId, photo.ThumbnailMediaId);

            await EmitProgressAsync(photo.Id, "processing", "ai-description");

            // Generate AI metadata asynchronously (with SignalR updates)
            _ = Task.Run(async () =>
            {
                try
                {
                    await GenerateAIMetadataAsync(photo, CancellationToken.None);

                    // Emit completion event after AI processing
                    await EmitProgressAsync(photo.Id, "completed", "completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate AI metadata for photo {PhotoId}", photo.Id);
                    await EmitProgressAsync(photo.Id, "failed", "ai-description", ex.Message);
                }
            }, CancellationToken.None);

            photo.ProcessingStatus = ProcessingStatus.Completed;
            await photo.Save(ct);

            // Emit progress for basic processing complete (AI still running in background)
            await EmitProgressAsync(photo.Id, "processing", "completed");

            return photo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process photo {FileName}", file.FileName);
            photo.ProcessingStatus = ProcessingStatus.Failed;

            await EmitProgressAsync(photo.Id ?? "", "failed", "upload", ex.Message);

            // Update job with error
            try
            {
                var job = await ProcessingJob.Get(jobId, CancellationToken.None);
                if (job != null)
                {
                    job.Errors.Add($"{file.FileName}: {ex.Message}");
                    await job.Save(CancellationToken.None);
                }
            }
            catch (Exception jobEx)
            {
                _logger.LogWarning(jobEx, "Failed to update job {JobId} with error", jobId);
            }

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

        // Structured AI analysis first (best semantic content)
        if (photo.AiAnalysis != null)
        {
            parts.Add(photo.AiAnalysis.ToEmbeddingText());
        }

        // Fallback to legacy fields if no structured analysis
        if (!string.IsNullOrEmpty(photo.OriginalFileName))
            parts.Add($"Filename: {photo.OriginalFileName}");

        if (photo.AutoTags.Any())
            parts.Add($"Tags: {string.Join(", ", photo.AutoTags)}");

        if (!string.IsNullOrEmpty(photo.MoodDescription))
            parts.Add($"Mood: {photo.MoodDescription}");

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
    /// Robust JSON parsing with multiple fallback strategies
    /// </summary>
    private AiAnalysis? ParseAiResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        // Strategy 1: Direct parse (model returned clean JSON)
        try
        {
            return JsonSerializer.Deserialize<AiAnalysis>(response);
        }
        catch
        {
            // Continue to fallback strategies
        }

        // Strategy 2: Strip markdown code blocks
        var cleanedResponse = Regex.Replace(
            response,
            @"```(?:json)?\s*|\s*```",
            "",
            RegexOptions.IgnoreCase | RegexOptions.Multiline
        ).Trim();

        try
        {
            return JsonSerializer.Deserialize<AiAnalysis>(cleanedResponse);
        }
        catch
        {
            // Continue to fallback strategies
        }

        // Strategy 3: Extract first JSON object from mixed content
        var jsonMatch = Regex.Match(
            cleanedResponse,
            @"\{[\s\S]*?\}(?=\s*(?:$|[^,\}\]]))",
            RegexOptions.Multiline
        );

        if (jsonMatch.Success)
        {
            try
            {
                return JsonSerializer.Deserialize<AiAnalysis>(jsonMatch.Value);
            }
            catch
            {
                // All strategies failed
            }
        }

        _logger.LogWarning("All JSON parsing strategies failed. Response preview: {Preview}",
            response.Length > 200 ? response.Substring(0, 200) + "..." : response);

        return null;
    }

    /// <summary>
    /// Generate structured AI analysis for a photo using vision model
    /// </summary>
    private async Task GenerateDetailedDescriptionAsync(PhotoAsset photo, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Generating structured AI analysis for photo {PhotoId}", photo.Id);

            // Load gallery image
            var gallery = await PhotoGallery.Get(photo.GalleryMediaId, ct);
            if (gallery == null)
            {
                _logger.LogWarning("Gallery image not found for photo {PhotoId}, skipping AI analysis", photo.Id);
                return;
            }

            await using var imageStream = await gallery.OpenRead(ct);
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, ct);
            var imageBytes = ms.ToArray();

            // Refined JSON prompt - captures detailed facts while maintaining accuracy
            var prompt = @"Analyze this photograph and return a JSON object. Be accurate and only describe what you clearly see.

Return ONLY valid JSON in this exact format (no markdown, no code blocks, just the JSON):

{
  ""tags"": [""tag1"", ""tag2"", ""tag3"", ""...""],
  ""summary"": ""One clear sentence describing the image"",
  ""facts"": {
    ""Type"": ""portrait|landscape|still-life|product|food|screenshot|architecture|wildlife|other"",
    ""Style"": ""photography|painting|digital-art|illustration|abstract|ingame-screenshot|other"",
    ""Subject Count"": ""describe number and type of subjects"",
    ""Composition"": ""describe framing and arrangement"",
    ""Palette"": ""list 3-5 dominant colors"",
    ""Lighting"": ""describe light source and quality"",
    ""Setting"": ""describe location context"",
    ""Mood"": ""describe emotional tone or atmosphere""
  }
}

IMPORTANT: Add additional fact fields for clearly visible details. Use descriptive field names.
Example with optional fields: { ""Character"": ""female, dark hair, elf ears"", ""Atmospherics"": ""soft fog, god rays"", ""Light Sources"": ""neon signs, LED panels"" }

GUIDELINES:
- tags: 6-10 searchable keywords (lowercase, use hyphens for multi-word like ""red-hoodie"")
  Examples: character, portrait, studio, graffiti, black-hoodie, red-headphones, cool-tones, centered

- summary: 30-80 words describing subject, action, setting, and visual characteristics
  Example: ""A female character with dark brown hair and elf-like ears, wearing a black hoodie and red headphones, stands against a graffiti-style backdrop in a cool-toned, evenly lit studio.""

- facts.Type: Main category of the photo
  Examples: portrait, landscape, still-life, product, food, screenshot, architecture, wildlife, macro, abstract

- facts.SubjectCount: Number and type of main subjects
  Examples: ""no subjects"", ""1 person"", ""2 people"", ""3 characters"", ""single object"", ""multiple items""

- facts.Composition: How elements are arranged
  Examples: centered, rule-of-thirds, symmetrical, diagonal, leading-lines, framed, off-center

- facts.Palette: 3-5 dominant colors (comma-separated)
  Examples: ""blue, gray, brown"", ""black, red, white"", ""warm-tones, orange, yellow""

- facts.Lighting: Light source and characteristics
  Examples: overcast, golden-hour, studio, natural, soft, dramatic, backlit, low-key, high-key

- facts.Setting: Location or environment context
  Examples: ""outdoor, castle"", ""indoor, studio"", ""urban, street"", ""nature, forest"", ""home, kitchen""

- facts.Mood: Emotional tone or atmosphere
  Examples: mysterious, cheerful, serene, dramatic, playful, somber, energetic, contemplative

- Additional facts: Include any other clearly visible details. Only add fields you can see - never force details that aren't there.
  * Character details: gender, hair, expression, clothing, accessories, pose
  * Locale cues: architecture style, props, vegetation, furniture, water, terrain
  * Topology: foreground/midground/background elements, platforms, stairs, bridges, paths
  * Atmospherics: fog, haze, smoke, sparks, rain, snow, bloom, god-rays
  * Color grade: warm/cool/neutral tints (teal-orange, magenta, sepia)
  * Time/Weather: day/night/sunset/overcast (if discernible)
  * Depth cues: bokeh, DOF blur, shallow/deep focus
  * Motion/VFX: motion blur, particles, energy effects, magic circles
  * Visible text: transcribe short, clearly readable text only
  * Light sources: torches, neon, sun, LEDs, screens, practicals

CRITICAL RULES:
1. Return ONLY the JSON object - no explanatory text
2. Only include what you can CLEARLY see - never guess or hallucinate
3. Use simple, factual descriptions
4. Keep all strings properly escaped for JSON
5. If uncertain about a field, use generic but accurate descriptions

Analyze the image and return the JSON now.";

            // Use vision model
            var visionOptions = new Koan.AI.Contracts.Options.AiVisionOptions
            {
                ImageBytes = imageBytes,
                Prompt = prompt,
                Model = "qwen2.5vl",
                Temperature = 0.7
            };

            var response = await Koan.AI.Ai.Understand(visionOptions, ct);

            // Parse JSON with robust error handling
            var analysis = ParseAiResponse(response);

            if (analysis == null)
            {
                _logger.LogWarning("Failed to parse AI response for photo {PhotoId}, using error state", photo.Id);
                analysis = AiAnalysis.CreateError("Failed to parse AI vision response");
            }

            // Store structured analysis
            photo.AiAnalysis = analysis;
            await photo.Save(ct);

            _logger.LogInformation(
                "Structured AI analysis generated for photo {PhotoId}: {TagCount} tags, {FactCount} facts",
                photo.Id, analysis.Tags.Count, analysis.Facts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI analysis for photo {PhotoId}, continuing without it", photo.Id);
            // Non-fatal - continue processing
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

    /// <summary>
    /// Update job progress by incrementing processed photo count
    /// </summary>
    private async Task UpdateJobProgressAsync(string jobId, CancellationToken ct)
    {
        try
        {
            var job = await ProcessingJob.Get(jobId, ct);
            if (job != null)
            {
                job.ProcessedPhotos++;

                // Update job status if all photos processed
                if (job.ProcessedPhotos >= job.TotalPhotos)
                {
                    job.Status = job.Errors.Count == 0
                        ? ProcessingStatus.Completed
                        : ProcessingStatus.PartialSuccess;
                    job.CompletedAt = DateTime.UtcNow;

                    // Emit job completion event
                    await _hubContext.Clients.Group($"job:{jobId}").SendAsync("JobCompleted", new JobCompletionEvent
                    {
                        JobId = jobId,
                        Status = job.Status == ProcessingStatus.Completed ? "completed" : "partial-success",
                        TotalPhotos = job.TotalPhotos,
                        SuccessCount = job.ProcessedPhotos - job.Errors.Count,
                        FailureCount = job.Errors.Count,
                        Errors = job.Errors
                    }, CancellationToken.None);
                }

                await job.Save(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update job progress for {JobId}", jobId);
        }
    }

    /// <summary>
    /// Update event photo count and status
    /// </summary>
    private async Task UpdateEventPhotoCountAsync(string eventId, CancellationToken ct)
    {
        try
        {
            var evt = await Event.Get(eventId, ct);
            if (evt != null)
            {
                var photos = await PhotoAsset.Query(p => p.EventId == eventId, ct);
                evt.PhotoCount = photos.Count;
                evt.ProcessingStatus = ProcessingStatus.Completed;
                await evt.Save(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update event photo count for {EventId}", eventId);
        }
    }
}
