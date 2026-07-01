using System.Diagnostics;
using System.Text.RegularExpressions;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Media.Core.Extensions;
using Koan.Media.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using S6.SnapVault.Media;
using S6.SnapVault.Models;
using S6.SnapVault.Progress;
using S6.SnapVault.Services.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace S6.SnapVault.Services;

/// <summary>
/// The greenfield ingest + AI pipeline (SnapVault step 5a). A thin service driven by the durable, tenant-carrying
/// <c>PhotoProcessingJob</c>: storage → EXIF → daily-event → AI vision analysis → embedding, all in the studio
/// that submitted the upload (ARCH-0100). Progress is reported through the <paramref name="reportProgress"/>
/// callback the job wires to <c>ctx.Progress</c> — persisted to the jobs ledger, streamed to the browser by the
/// step-4 SSE projection. No SignalR, no separate batch-tracker entity.
///
/// <para>Two deliberate changes from the legacy pipeline: (1) the 4 eager derivative entities are gone (step 3
/// replaced them with on-demand <c>[MediaRecipe]</c>s), so the AI vision source is <b>re-sourced</b> by rendering
/// the <c>gallery</c> recipe in-process from the single stored original; (2) embedding is attribute-driven
/// (<c>[Embedding]</c> on <see cref="PhotoAsset"/>) — saving the enriched photo queues it.</para>
/// </summary>
internal sealed class PhotoProcessingService : IPhotoProcessingService
{
    private readonly ILogger<PhotoProcessingService> _logger;
    private readonly IAnalysisPromptFactory _promptFactory;

    public PhotoProcessingService(ILogger<PhotoProcessingService> logger, IAnalysisPromptFactory promptFactory)
    {
        _logger = logger;
        _promptFactory = promptFactory;
    }

    public async Task<PhotoAsset> ProcessUpload(
        string? eventId, Stream content, string fileName, string contentType,
        Func<double, string, Task>? reportProgress, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing upload: {FileName} for event {EventId}", fileName, eventId ?? "auto");

        async Task Progress(double fraction, string stage)
        {
            if (reportProgress is null) return;
            try { await reportProgress(fraction, stage); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to report progress ({Stage})", stage); }
        }

        await Progress(0.05, PhotoProcessingStage.Upload);

        // Buffer the raw content into a seekable stream (the staging blob may be forward-only; the pipeline
        // re-reads from position 0 for dimensions, EXIF, and the full-resolution upload).
        using var sourceStream = new MemoryStream();
        await content.CopyToAsync(sourceStream, ct);
        sourceStream.Position = 0;

        // Dimensions (identify, no full decode).
        int width, height;
        using (var dimensionCheck = new MemoryStream())
        {
            await sourceStream.CopyToAsync(dimensionCheck, ct);
            dimensionCheck.Position = 0;
            sourceStream.Position = 0;
            var info = await Image.IdentifyAsync(dimensionCheck, ct);
            width = info.Width;
            height = info.Height;
        }

        await Progress(0.25, PhotoProcessingStage.Exif);

        // EXIF (camera, capture date, GPS) — read into a scratch entity, then applied to the stored photo below.
        var scratch = new PhotoAsset { Width = width, Height = height };
        await ExtractExifMetadata(scratch, sourceStream, ct);
        sourceStream.Position = 0;

        // Auto-assign a daily album from the capture date when no event was chosen (INV-2 half-open UTC range).
        var resolvedEventId = eventId;
        if (string.IsNullOrEmpty(resolvedEventId))
        {
            var eventDate = scratch.CapturedAt ?? DateTime.UtcNow;
            var dailyEvent = await GetOrCreateDailyEvent(eventDate, ct);
            resolvedEventId = dailyEvent.Id;
            _logger.LogInformation("Auto-assigned photo to daily event: {EventName}", dailyEvent.Name);
        }

        // Store the full-resolution original (creates the PhotoAsset + blob), then enrich it with metadata.
        var photo = await PhotoAsset.Upload(sourceStream, fileName, contentType, ct: ct);
        photo.EventId = resolvedEventId!;
        photo.OriginalFileName = fileName;
        photo.UploadedAt = DateTime.UtcNow;
        photo.Width = width;
        photo.Height = height;
        photo.CameraModel = scratch.CameraModel;
        photo.LensModel = scratch.LensModel;
        photo.FocalLength = scratch.FocalLength;
        photo.Aperture = scratch.Aperture;
        photo.ShutterSpeed = scratch.ShutterSpeed;
        photo.ISO = scratch.ISO;
        photo.CapturedAt = scratch.CapturedAt;
        photo.Location = scratch.Location;
        photo.ProcessingStatus = ProcessingStatus.InProgress;
        await photo.Save(ct);

        _logger.LogInformation("Photo stored: {PhotoId} ({Width}x{Height})", photo.Id, photo.Width, photo.Height);

        await Progress(0.5, PhotoProcessingStage.AiDescription);

        // Generate AI metadata + embedding INLINE — durable and tenant-carried. Non-fatal: the upload (storage +
        // EXIF) already succeeded and the photo is usable; AI can be regenerated later via /regenerate-ai-analysis.
        try
        {
            await Progress(0.85, PhotoProcessingStage.Embedding);
            await GenerateAIMetadata(photo, ct);
            await Progress(1.0, PhotoProcessingStage.Completed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI metadata generation failed for photo {PhotoId}; stored without analysis", photo.Id);
        }

        // Keep the owning event's photo count fresh.
        await UpdateEventPhotoCount(photo.EventId, ct);

        return photo;
    }

    public async Task<PhotoAsset> GenerateAIMetadata(PhotoAsset photo, CancellationToken ct = default)
    {
        // Transaction coordination: atomic commit across entity + vector operations. The [Embedding] attribute
        // handles embedding generation + vectorization automatically via lifecycle hooks on Save.
        using var tx = EntityContext.Transaction($"ai-metadata-{photo.Id}");

        try
        {
            _logger.LogInformation("Generating AI metadata for photo {PhotoId}", photo.Id);

            await GenerateDetailedDescription(photo, null, ct);

            photo.ProcessingStatus = ProcessingStatus.Completed;
            await photo.Save(ct);

            await EntityContext.Commit(ct);

            _logger.LogInformation("AI metadata generated for photo {PhotoId} (attribute-driven, transactional)", photo.Id);
            return photo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI metadata for photo {PhotoId}, rolling back transaction", photo.Id);
            await EntityContext.Rollback(ct);

            photo.ProcessingStatus = ProcessingStatus.Failed;
            await photo.Save(ct);
            throw;
        }
    }

    public async Task<List<PhotoAsset>> SemanticSearch(string query, string? eventId = null, double alpha = 0.5, int topK = 20, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Semantic search: query='{Query}' alpha={Alpha} eventId={EventId} topK={TopK}", query, alpha, eventId, topK);

            if (!Vector<PhotoAsset>.IsAvailable)
            {
                _logger.LogWarning("Vector search unavailable, falling back to keyword search");
                return await FallbackKeywordSearch(query, eventId, topK, ct);
            }

            var queryVector = await Client.Embed(query, ct);

            // Hybrid vector search with user-controlled alpha (0.0 = keyword, 1.0 = semantic).
            var vectorResults = await Vector<PhotoAsset>.Search(
                vector: queryVector,
                text: query,
                alpha: alpha,
                topK: topK,
                ct: ct);

            var photos = new List<PhotoAsset>();
            foreach (var match in vectorResults.Matches)
            {
                var photo = await PhotoAsset.Get(match.Id, ct);
                if (photo != null && (string.IsNullOrEmpty(eventId) || photo.EventId == eventId))
                {
                    photos.Add(photo);
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
        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery)) return new List<PhotoAsset>();

        var queryLower = normalizedQuery.ToLower();
        var photos = await PhotoAsset.Query(p =>
            (string.IsNullOrEmpty(eventId) || p.EventId == eventId) &&
            ((p.OriginalFileName != null && p.OriginalFileName.ToLower().Contains(queryLower)) ||
             (p.AutoTags != null && p.AutoTags.Any(t => t != null && t.ToLower().Contains(queryLower))) ||
             (p.MoodDescription != null && p.MoodDescription.ToLower().Contains(queryLower))), ct);

        return photos.Take(topK).ToList();
    }

    private async Task ExtractExifMetadata(PhotoAsset photo, Stream stream, CancellationToken ct)
    {
        try
        {
            stream.Position = 0;
            using var image = await Image.LoadAsync(stream, ct);

            var exif = image.Metadata.ExifProfile;
            if (exif == null) return;

            if (exif.TryGetValue(ExifTag.Model, out var modelValue))
                photo.CameraModel = modelValue.Value?.ToString();
            if (exif.TryGetValue(ExifTag.LensModel, out var lensValue))
                photo.LensModel = lensValue.Value?.ToString();
            if (exif.TryGetValue(ExifTag.FocalLength, out var focalLengthValue))
                photo.FocalLength = $"{focalLengthValue.Value}mm";
            if (exif.TryGetValue(ExifTag.FNumber, out var apertureValue))
                photo.Aperture = $"f/{apertureValue.Value}";
            if (exif.TryGetValue(ExifTag.ExposureTime, out var shutterValue))
                photo.ShutterSpeed = shutterValue.Value.ToString();
            if (exif.TryGetValue(ExifTag.ISOSpeedRatings, out var isoValue) && isoValue.Value is ushort[] isoArray && isoArray.Length > 0)
                photo.ISO = isoArray[0];

            if (exif.TryGetValue(ExifTag.DateTimeOriginal, out var dateValue) &&
                dateValue.Value != null &&
                DateTime.TryParse(dateValue.Value.ToString(), out var capturedAt))
            {
                photo.CapturedAt = DateTime.SpecifyKind(capturedAt, DateTimeKind.Utc);
            }

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

            _logger.LogDebug("EXIF extracted: Camera={Camera} ISO={ISO} Date={Date}", photo.CameraModel, photo.ISO, photo.CapturedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract EXIF metadata");
        }
    }

    private static double ConvertToDecimalDegrees(Rational[] coordinate)
    {
        if (coordinate == null || coordinate.Length != 3) return 0;
        return coordinate[0].ToDouble() + (coordinate[1].ToDouble() / 60.0) + (coordinate[2].ToDouble() / 3600.0);
    }

    /// <summary>Robust JSON parsing using JObject navigation (INV-1: fact keys normalized to lowercase).</summary>
    private AiAnalysis? ParseAiResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        var jsonText = response;
        var json = TryParseJson(jsonText);

        if (json == null)
        {
            jsonText = Regex.Replace(response, @"```(?:json)?\s*|\s*```", "", RegexOptions.IgnoreCase | RegexOptions.Multiline).Trim();
            json = TryParseJson(jsonText);
        }
        if (json == null)
        {
            jsonText = ExtractJsonByBalancedBraces(jsonText);
            if (!string.IsNullOrEmpty(jsonText)) json = TryParseJson(jsonText);
        }
        if (json == null)
        {
            _logger.LogWarning("All JSON parsing strategies failed. Response preview: {Preview}",
                response.Length > 200 ? response.Substring(0, 200) + "..." : response);
            return null;
        }

        try
        {
            var analysis = new AiAnalysis();

            if (json["tags"] is JArray tagsArray)
            {
                var tags = tagsArray.ToObject<List<string>>() ?? new List<string>();
                analysis.Tags = tags
                    .Select(t => t?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToList();
            }

            if (json["summary"]?.Type == JTokenType.String)
                analysis.Summary = json["summary"]?.ToString() ?? "";

            if (json["facts"] is JObject factsObject)
            {
                analysis.Facts = new Dictionary<string, string>();
                foreach (var property in factsObject.Properties())
                {
                    var value = property.Value;
                    var normalizedKey = property.Name.ToLowerInvariant();   // INV-1

                    if (value.Type == JTokenType.Array)
                    {
                        var arrayValues = value.ToObject<List<string>>() ?? new List<string>();
                        var uniqueValues = arrayValues
                            .Select(v => v?.Trim())
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        analysis.Facts[normalizedKey] = string.Join(", ", uniqueValues);
                    }
                    else if (value.Type == JTokenType.String)
                    {
                        analysis.Facts[normalizedKey] = value.ToString();
                        _logger.LogWarning("Fact '{FactName}' was string instead of array - model didn't follow prompt", normalizedKey);
                    }
                    else
                    {
                        analysis.Facts[normalizedKey] = value.ToString();
                    }
                }
            }

            _logger.LogInformation("Successfully parsed AI response: {TagCount} tags, {FactCount} facts", analysis.Tags.Count, analysis.Facts.Count);
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to navigate JObject structure");
            return null;
        }
    }

    private JObject? TryParseJson(string text)
    {
        try { return JObject.Parse(text); }
        catch { return null; }
    }

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
            if (braceCount == 0) return text.Substring(startIndex, i - startIndex + 1);
        }
        return "";
    }

    /// <summary>
    /// Generate structured AI analysis for a photo using the vision model. The vision byte source is RE-SOURCED
    /// (step-5 / spec §8-6): the legacy read a <c>PhotoGallery</c> derivative entity (now deleted) — here the
    /// gallery recipe is rendered in-process from the single stored original, so the model sees the same 1200px
    /// downscale without materializing a derivative.
    /// </summary>
    private async Task GenerateDetailedDescription(PhotoAsset photo, string? analysisStyleId = null, CancellationToken ct = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            var styleEntity = await ResolveAnalysisStyle(analysisStyleId, photo, ct);
            var effectiveStyleName = styleEntity?.Name ?? "default";

            _logger.LogInformation("Generating structured AI analysis for photo {PhotoId} with style '{Style}'", photo.Id, effectiveStyleName);

            // Vision re-source: render the gallery recipe (1200px) in-process from the stored original.
            byte[] imageBytes;
            await using (var original = await photo.OpenRead(ct))
            using (var galleryBuffer = new MemoryStream())
            {
                await original.AsMedia().Apply(PhotoRecipes.Gallery()).WriteToAsync(galleryBuffer, ct);
                imageBytes = galleryBuffer.ToArray();
            }

            var context = new PhotoContext(
                PhotoId: photo.Id,
                Width: photo.Width,
                Height: photo.Height,
                AspectRatio: photo.Height == 0 ? 1.0 : (double)photo.Width / photo.Height,
                CameraModel: photo.CameraModel,
                CapturedAt: photo.CapturedAt,
                ExifData: null);

            string prompt;
            if (styleEntity == null)
            {
                prompt = _promptFactory.RenderPrompt();
            }
            else if (styleEntity.IsSmartStyle)
            {
                var detectedStyle = await ClassifyImageStyle(photo, imageBytes, ct);
                prompt = _promptFactory.RenderPromptFor(detectedStyle);
                effectiveStyleName = $"smart→{detectedStyle.Name}";
            }
            else
            {
                prompt = _promptFactory.RenderPromptFor(styleEntity);
            }

            prompt = _promptFactory.SubstituteVariables(prompt, context);

            var response = await Client.Chat(prompt, new ChatOptions
            {
                Image = imageBytes,
                ImageMimeType = "image/jpeg"
            }, ct);

            var analysis = ParseAiResponse(response);
            if (analysis == null)
            {
                _logger.LogWarning("Failed to parse AI response for photo {PhotoId}, using error state", photo.Id);
                analysis = AiAnalysis.CreateError("Failed to parse AI vision response");
            }

            analysis.AnalysisStyle = styleEntity?.Id ?? "default";
            analysis.AnalyzedAt = DateTime.UtcNow;
            analysis.TokensUsed = null;

            photo.AiAnalysis = analysis;
            await photo.Save(ct);

            stopwatch.Stop();
            _logger.LogInformation("Structured AI analysis generated for photo {PhotoId} in {ElapsedMs}ms: style={Style}, tags={TagCount}, facts={FactCount}",
                photo.Id, stopwatch.ElapsedMilliseconds, effectiveStyleName, analysis.Tags.Count, analysis.Facts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI analysis for photo {PhotoId}, continuing without it", photo.Id);
        }
    }

    /// <summary>Resolve the analysis style entity: explicit request → last used → null (default base prompt).</summary>
    private async Task<AnalysisStyle?> ResolveAnalysisStyle(string? requestedId, PhotoAsset photo, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(requestedId))
        {
            var requested = await AnalysisStyle.Get(requestedId, ct);
            if (requested != null && requested.IsActive) return requested;
            _logger.LogWarning("Invalid or inactive analysis style '{StyleId}' requested, falling back to default", requestedId);
        }

        if (!string.IsNullOrEmpty(photo.AiAnalysis?.AnalysisStyle))
        {
            var lastUsed = await AnalysisStyle.Get(photo.AiAnalysis.AnalysisStyle, ct);
            if (lastUsed != null && lastUsed.IsActive) return lastUsed;
        }

        return null;
    }

    /// <summary>Classify image style for smart mode (two-stage). Caches the result in PhotoAsset.InferredStyleId.</summary>
    private async Task<AnalysisStyle> ClassifyImageStyle(PhotoAsset photo, byte[] imageBytes, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(photo.InferredStyleId))
        {
            var cached = await AnalysisStyle.Get(photo.InferredStyleId, ct);
            if (cached != null && cached.IsActive && !cached.IsSmartStyle)
            {
                _logger.LogDebug("Using cached style inference for photo {PhotoId}: {StyleName}", photo.Id, cached.Name);
                return cached;
            }
        }

        var availableStyles = await AnalysisStyle.Query(s => !s.IsSmartStyle && s.IsActive && s.IsSystemStyle, ct);
        if (!availableStyles.Any())
        {
            _logger.LogWarning("No styles available for classification, using first active style");
            var fallback = await AnalysisStyle.Query(s => s.IsActive, ct);
            return fallback.FirstOrDefault() ?? throw new InvalidOperationException("No active analysis styles found");
        }

        var classificationPrompt = _promptFactory.GetClassificationPrompt(availableStyles);
        var classificationResponse = await Client.Chat(classificationPrompt, new ChatOptions
        {
            Image = imageBytes,
            ImageMimeType = "image/jpeg"
        }, ct);

        var detectedStyleName = classificationResponse.Trim().ToLower();
        var detectedStyle = availableStyles.FirstOrDefault(s =>
            s.Name.ToLower().Contains(detectedStyleName) || s.Id.ToLower() == detectedStyleName);

        if (detectedStyle == null)
        {
            _logger.LogWarning("Classification returned unrecognized style '{DetectedStyle}', defaulting to first style", detectedStyleName);
            detectedStyle = availableStyles.OrderBy(s => s.Priority).First();
        }

        photo.InferredStyleId = detectedStyle.Id;
        photo.InferredAt = DateTime.UtcNow;
        await photo.Save(ct);

        _logger.LogInformation("Classified photo {PhotoId} as '{StyleName}'", photo.Id, detectedStyle.Name);
        return detectedStyle;
    }

    /// <summary>Get or create a daily event for the given date (INV-2: half-open UTC day-range, never <c>.Date ==</c>).</summary>
    private async Task<Event> GetOrCreateDailyEvent(DateTime date, CancellationToken ct)
    {
        var normalizedDate = date.Date;
        var eventName = normalizedDate.ToString("MMMM d, yyyy");

        // INV-2: inclusive/exclusive UTC day range (avoids the Mongo translator emitting unsupported $dateTrunc).
        var dayStart = normalizedDate;
        var dayEndExclusive = dayStart.AddDays(1);
        var existingEvents = await Event.Query(e =>
            e.Type == EventType.DailyAuto &&
            e.EventDate >= dayStart &&
            e.EventDate < dayEndExclusive, ct);

        if (existingEvents.Any()) return existingEvents.First();

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

    private async Task UpdateEventPhotoCount(string eventId, CancellationToken ct)
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

    /// <summary>Regenerate AI analysis while preserving locked facts + summary ("reroll with holds").</summary>
    public async Task<PhotoAsset> RegenerateAIAnalysis(string photoId, string? analysisStyle = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Regenerating AI analysis for photo {PhotoId}", photoId);

        var photo = await PhotoAsset.Get(photoId, ct);
        if (photo == null) throw new InvalidOperationException($"Photo {photoId} not found");

        using var tx = EntityContext.Transaction($"ai-regen-{photoId}");
        try
        {
            // 1. Buffer locked content before regeneration.
            string? lockedSummary = null;
            var summaryWasLocked = false;
            if (photo.AiAnalysis?.SummaryLocked == true)
            {
                lockedSummary = photo.AiAnalysis.Summary;
                summaryWasLocked = true;
            }

            var lockedFacts = new Dictionary<string, string>();
            if (photo.AiAnalysis?.LockedFactKeys != null)
            {
                foreach (var factKey in photo.AiAnalysis.LockedFactKeys)
                {
                    if (photo.AiAnalysis.Facts.TryGetValue(factKey, out var value))
                        lockedFacts[factKey] = value;
                }
            }

            // 2. Regenerate (replaces photo.AiAnalysis).
            await GenerateDetailedDescription(photo, analysisStyle, ct);

            // 3. Restore locked content.
            if (photo.AiAnalysis != null)
            {
                if (summaryWasLocked && lockedSummary != null)
                {
                    photo.AiAnalysis.Summary = lockedSummary;
                    photo.AiAnalysis.SummaryLocked = true;
                }
                if (lockedFacts.Count > 0)
                {
                    foreach (var (factKey, value) in lockedFacts)
                        photo.AiAnalysis.Facts[factKey] = value;
                    photo.AiAnalysis.LockedFactKeys = new HashSet<string>(lockedFacts.Keys);
                }
            }

            // 4. Save with automatic re-embedding (attribute-driven).
            await photo.Save(ct);
            await EntityContext.Commit(ct);

            _logger.LogInformation("AI analysis regenerated for photo {PhotoId} (transactional)", photoId);
            return photo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate AI analysis for photo {PhotoId}, rolling back transaction", photoId);
            await EntityContext.Rollback(ct);
            throw;
        }
    }
}
