using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using S6.SnapVault.Models;
using S6.SnapVault.Hubs;
using S6.SnapVault.Services.AI;
using Koan.Media.Core.Extensions;
using Koan.AI;
using Koan.Data.Core;
using Koan.Data.Vector;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace S6.SnapVault.Services;

/// <summary>
/// Photo processing service using DX-0047 Fluent Media Transform API
/// </summary>
internal sealed class PhotoProcessingService : IPhotoProcessingService
{
    private readonly ILogger<PhotoProcessingService> _logger;
    private readonly IHubContext<PhotoProcessingHub> _hubContext;
    private readonly IAnalysisPromptFactory _promptFactory;

    public PhotoProcessingService(
        ILogger<PhotoProcessingService> logger,
        IHubContext<PhotoProcessingHub> hubContext,
        IAnalysisPromptFactory promptFactory)
    {
        _logger = logger;
        _hubContext = hubContext;
        _promptFactory = promptFactory;
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
            await GenerateDetailedDescriptionAsync(photo, null, ct);

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
            await VectorData<PhotoAsset>.SaveWithVector(photo, embedding, vectorMetadata, ct);

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
    /// Robust JSON parsing using JObject navigation
    /// </summary>
    private AiAnalysis? ParseAiResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        string jsonText = response;

        // Strategy 1: Try direct parse
        JObject? json = TryParseJson(jsonText);

        if (json == null)
        {
            // Strategy 2: Strip markdown code blocks (```json ... ``` or ``` ... ```)
            jsonText = Regex.Replace(
                response,
                @"```(?:json)?\s*|\s*```",
                "",
                RegexOptions.IgnoreCase | RegexOptions.Multiline
            ).Trim();

            json = TryParseJson(jsonText);
        }

        if (json == null)
        {
            // Strategy 3: Extract JSON by finding balanced braces
            jsonText = ExtractJsonByBalancedBraces(jsonText);
            if (!string.IsNullOrEmpty(jsonText))
            {
                json = TryParseJson(jsonText);
            }
        }

        if (json == null)
        {
            _logger.LogWarning("All JSON parsing strategies failed. Response preview: {Preview}",
                response.Length > 200 ? response.Substring(0, 200) + "..." : response);
            return null;
        }

        // Navigate JObject to build AiAnalysis
        try
        {
            var analysis = new AiAnalysis();

            // Extract tags array
            if (json["tags"] is JArray tagsArray)
            {
                var tags = tagsArray.ToObject<List<string>>() ?? new List<string>();

                // Deduplicate tags (case-insensitive) and remove empty values
                analysis.Tags = tags
                    .Select(t => t?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToList();
            }

            // Extract summary string
            if (json["summary"]?.Type == JTokenType.String)
            {
                analysis.Summary = json["summary"]?.ToString() ?? "";
            }

            // Extract facts object - ALL values should be arrays now
            if (json["facts"] is JObject factsObject)
            {
                analysis.Facts = new Dictionary<string, string>();

                foreach (var property in factsObject.Properties())
                {
                    var value = property.Value;
                    // Normalize fact key to lowercase
                    var normalizedKey = property.Name.ToLowerInvariant();

                    // Facts are now arrays - convert to comma-separated string for storage
                    if (value.Type == JTokenType.Array)
                    {
                        var arrayValues = value.ToObject<List<string>>() ?? new List<string>();

                        // Deduplicate values (case-insensitive) - common with "visible text" repeating
                        var uniqueValues = arrayValues
                            .Select(v => v?.Trim())
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        analysis.Facts[normalizedKey] = string.Join(", ", uniqueValues);
                    }
                    // Fallback for legacy/malformed responses (single strings)
                    else if (value.Type == JTokenType.String)
                    {
                        analysis.Facts[normalizedKey] = value.ToString();
                        _logger.LogWarning("Fact '{FactName}' was string instead of array - model didn't follow prompt", normalizedKey);
                    }
                    else
                    {
                        // Convert other types to string
                        analysis.Facts[normalizedKey] = value.ToString();
                    }
                }
            }

            _logger.LogInformation("Successfully parsed AI response: {TagCount} tags, {FactCount} facts",
                analysis.Tags.Count, analysis.Facts.Count);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to navigate JObject structure");
            return null;
        }
    }

    /// <summary>
    /// Try to parse JSON text into JObject, returns null if failed
    /// </summary>
    private JObject? TryParseJson(string text)
    {
        try
        {
            return JObject.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract JSON object by counting balanced braces
    /// </summary>
    private string ExtractJsonByBalancedBraces(string text)
    {
        int startIndex = text.IndexOf('{');
        if (startIndex < 0) return "";

        int braceCount = 0;
        for (int i = startIndex; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '{') braceCount++;
            else if (c == '}') braceCount--;

            if (braceCount == 0)
            {
                // Found matching closing brace
                return text.Substring(startIndex, i - startIndex + 1);
            }
        }

        return ""; // No balanced JSON found
    }

    /// <summary>
    /// Generate structured AI analysis for a photo using vision model
    /// Uses factory pattern for prompt assembly with entity-based style customization
    /// </summary>
    private async Task GenerateDetailedDescriptionAsync(PhotoAsset photo, string? analysisStyleId = null, CancellationToken ct = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Resolve analysis style entity (requested → last used → null=default)
            var styleEntity = await ResolveAnalysisStyleAsync(analysisStyleId, photo, ct);
            var effectiveStyleName = styleEntity?.Name ?? "default";

            _logger.LogInformation(
                "Generating structured AI analysis for photo {PhotoId} with style '{Style}'",
                photo.Id, effectiveStyleName);

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

            // Build photo context for variable substitution
            var context = new PhotoContext(
                PhotoId: photo.Id,
                Width: photo.Width,
                Height: photo.Height,
                AspectRatio: (double)photo.Width / photo.Height,
                CameraModel: photo.CameraModel,
                CapturedAt: photo.CapturedAt,
                ExifData: null
            );

            // Assemble prompt from factory
            string prompt;
            if (styleEntity == null)
            {
                // Default: base prompt only
                prompt = _promptFactory.RenderPrompt();
            }
            else if (styleEntity.IsSmartStyle)
            {
                // Smart mode: classify first, then render for detected style
                var detectedStyle = await ClassifyImageStyleAsync(photo, imageBytes, ct);
                prompt = _promptFactory.RenderPromptFor(detectedStyle);
                effectiveStyleName = $"smart→{detectedStyle.Name}";
            }
            else
            {
                // Direct style selection
                prompt = _promptFactory.RenderPromptFor(styleEntity);
            }

            // Apply variable substitution
            prompt = _promptFactory.SubstituteVariables(prompt, context);

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

            // Update analysis metadata
            analysis.AnalysisStyle = styleEntity?.Id ?? "default";
            analysis.AnalyzedAt = DateTime.UtcNow;
            analysis.TokensUsed = null; // TODO: Extract from response if available

            // Store structured analysis
            photo.AiAnalysis = analysis;
            await photo.Save(ct);

            stopwatch.Stop();

            _logger.LogInformation(
                "Structured AI analysis generated for photo {PhotoId} in {ElapsedMs}ms: style={Style}, tags={TagCount}, facts={FactCount}",
                photo.Id, stopwatch.ElapsedMilliseconds, effectiveStyleName, analysis.Tags.Count, analysis.Facts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI analysis for photo {PhotoId}, continuing without it", photo.Id);
            // Non-fatal - continue processing
        }
    }

    /// <summary>
    /// Resolve the analysis style entity to use based on priority: explicit request → last used → null (default)
    /// </summary>
    private async Task<AnalysisStyle?> ResolveAnalysisStyleAsync(string? requestedId, PhotoAsset photo, CancellationToken ct)
    {
        // Priority 1: Explicit request
        if (!string.IsNullOrEmpty(requestedId))
        {
            var requested = await AnalysisStyle.Get(requestedId, ct);
            if (requested != null && requested.IsActive)
            {
                return requested;
            }
            else
            {
                _logger.LogWarning("Invalid or inactive analysis style '{StyleId}' requested, falling back to default", requestedId);
            }
        }

        // Priority 2: Last used style from existing analysis
        if (!string.IsNullOrEmpty(photo.AiAnalysis?.AnalysisStyle))
        {
            var lastUsed = await AnalysisStyle.Get(photo.AiAnalysis.AnalysisStyle, ct);
            if (lastUsed != null && lastUsed.IsActive)
            {
                return lastUsed;
            }
        }

        // Priority 3: Null = use default base prompt (no customization)
        return null;
    }

    /// <summary>
    /// Classify image style for smart mode using two-stage analysis
    /// Caches result in PhotoAsset.InferredStyleId to avoid repeated classification
    /// </summary>
    private async Task<AnalysisStyle> ClassifyImageStyleAsync(PhotoAsset photo, byte[] imageBytes, CancellationToken ct)
    {
        // Check cache first
        if (!string.IsNullOrEmpty(photo.InferredStyleId))
        {
            var cached = await AnalysisStyle.Get(photo.InferredStyleId, ct);
            if (cached != null && cached.IsActive && !cached.IsSmartStyle)
            {
                _logger.LogDebug("Using cached style inference for photo {PhotoId}: {StyleName}", photo.Id, cached.Name);
                return cached;
            }
        }

        // Get available styles for classification (exclude smart itself)
        var availableStyles = await AnalysisStyle.Query(s =>
            !s.IsSmartStyle && s.IsActive && s.IsSystemStyle, ct);

        if (!availableStyles.Any())
        {
            _logger.LogWarning("No styles available for classification, using first active style");
            var fallback = await AnalysisStyle.Query(s => s.IsActive, ct);
            return fallback.FirstOrDefault() ?? throw new InvalidOperationException("No active analysis styles found");
        }

        // Generate classification prompt
        var classificationPrompt = _promptFactory.GetClassificationPrompt(availableStyles);

        _logger.LogDebug("Classifying image style for photo {PhotoId}...", photo.Id);
        var classificationStart = Stopwatch.StartNew();

        // Call AI for classification
        var classificationOptions = new Koan.AI.Contracts.Options.AiVisionOptions
        {
            ImageBytes = imageBytes,
            Prompt = classificationPrompt,
            Model = "qwen2.5vl",
            Temperature = 0.3 // Lower temperature for more consistent classification
        };

        var classificationResponse = await Koan.AI.Ai.Understand(classificationOptions, ct);
        classificationStart.Stop();

        // Parse classification result (should be style name like "portrait" or "landscape")
        var detectedStyleName = classificationResponse.Trim().ToLower();

        // Find matching style
        var detectedStyle = availableStyles.FirstOrDefault(s =>
            s.Name.ToLower().Contains(detectedStyleName) ||
            s.Id.ToLower() == detectedStyleName);

        if (detectedStyle == null)
        {
            _logger.LogWarning(
                "Classification returned unrecognized style '{DetectedStyle}', defaulting to first style",
                detectedStyleName);
            detectedStyle = availableStyles.OrderBy(s => s.Priority).First();
        }

        // Cache the inference
        photo.InferredStyleId = detectedStyle.Id;
        photo.InferredAt = DateTime.UtcNow;
        await photo.Save(ct);

        _logger.LogInformation(
            "Classified photo {PhotoId} as '{StyleName}' in {ElapsedMs}ms",
            photo.Id, detectedStyle.Name, classificationStart.ElapsedMilliseconds);

        return detectedStyle;
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

    /// <summary>
    /// Regenerate AI analysis for a photo while preserving locked facts
    /// "Reroll with holds" mechanic - locked facts are buffered and reapplied after regeneration
    /// </summary>
    public async Task<PhotoAsset> RegenerateAIAnalysisAsync(string photoId, string? analysisStyle = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Regenerating AI analysis for photo {PhotoId}", photoId);

        var photo = await PhotoAsset.Get(photoId, ct);
        if (photo == null)
        {
            throw new InvalidOperationException($"Photo {photoId} not found");
        }

        // 1. Buffer locked content before regeneration
        // Buffer locked summary
        string? lockedSummary = null;
        bool summaryWasLocked = false;

        if (photo.AiAnalysis?.SummaryLocked == true)
        {
            lockedSummary = photo.AiAnalysis.Summary;
            summaryWasLocked = true;
            _logger.LogDebug("Buffered locked summary: {Summary}", lockedSummary);
        }

        // Buffer locked facts (all fact keys are lowercase, so we can use direct dictionary access)
        var lockedFacts = new Dictionary<string, string>();

        if (photo.AiAnalysis?.LockedFactKeys != null)
        {
            foreach (var factKey in photo.AiAnalysis.LockedFactKeys)
            {
                if (photo.AiAnalysis.Facts.TryGetValue(factKey, out var value))
                {
                    lockedFacts[factKey] = value;
                    _logger.LogDebug("Buffered locked fact: {FactKey} = {FactValue}", factKey, value);
                }
            }
        }

        // 2. Regenerate AI analysis (this will replace photo.AiAnalysis)
        await GenerateDetailedDescriptionAsync(photo, analysisStyle, ct);

        // 3. Restore locked content (add them back if missing, overwrite if present)
        if (photo.AiAnalysis != null)
        {
            // Restore locked summary
            if (summaryWasLocked && lockedSummary != null)
            {
                photo.AiAnalysis.Summary = lockedSummary;
                photo.AiAnalysis.SummaryLocked = true;
                _logger.LogDebug("Restored locked summary");
            }

            // Restore locked facts
            if (lockedFacts.Count > 0)
            {
                foreach (var (factKey, value) in lockedFacts)
                {
                    // Add or overwrite the fact with the locked value
                    photo.AiAnalysis.Facts[factKey] = value;
                    _logger.LogDebug("Restored locked fact: {FactKey} = {FactValue}", factKey, value);
                }

                // Restore locked keys set
                photo.AiAnalysis.LockedFactKeys = new HashSet<string>(lockedFacts.Keys);
            }
        }

        // 4. Regenerate embedding with the merged facts
        var embeddingText = BuildEmbeddingText(photo);
        var embedding = await Koan.AI.Ai.Embed(embeddingText, ct);

        var vectorMetadata = new Dictionary<string, object>
        {
            ["originalFileName"] = photo.OriginalFileName,
            ["eventId"] = photo.EventId,
            ["searchText"] = embeddingText
        };

    await VectorData<PhotoAsset>.SaveWithVector(photo, embedding, vectorMetadata, ct);

        var lockedItems = new List<string>();
        if (summaryWasLocked) lockedItems.Add("summary");
        if (lockedFacts.Count > 0) lockedItems.Add($"{lockedFacts.Count} facts");

        if (lockedItems.Count > 0)
        {
            _logger.LogInformation("AI analysis regenerated for photo {PhotoId} with locked {Items} preserved",
                photoId, string.Join(" and ", lockedItems));
        }
        else
        {
            _logger.LogInformation("AI analysis regenerated for photo {PhotoId}", photoId);
        }

        return photo;
    }
}
