using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.AI.Attributes;

namespace Koan.Data.AI.Workers;

/// <summary>
/// Per-mode analysis dispatch engine.
/// Executes each requested <see cref="MediaAnalysis"/> mode against the AI Client,
/// writing results back to entity properties via reflection.
/// Returns per-mode completion status for partial-success tracking.
/// </summary>
internal static class MediaAnalysisExecutor
{
    /// <summary>
    /// Executes all analysis modes specified in <paramref name="metadata"/> against the
    /// provided <paramref name="content"/> bytes, writing results into <paramref name="entity"/> properties.
    /// </summary>
    public static async Task<Dictionary<MediaAnalysis, ModeStatus>> Execute<TEntity>(
        TEntity entity,
        MediaAnalysisMetadata metadata,
        byte[] content,
        CancellationToken ct) where TEntity : class
    {
        var results = new Dictionary<MediaAnalysis, ModeStatus>();
        var now = DateTimeOffset.UtcNow;

        if (metadata.Analysis.HasFlag(MediaAnalysis.Describe))
        {
            try
            {
                var description = await Client.Chat(
                    "Describe this image in detail, including objects, scene, mood, and notable features.",
                    new ChatOptions { Image = content },
                    ct);
                SetProperty(entity, metadata.DescriptionProperty, description);
                results[MediaAnalysis.Describe] = new ModeStatus(true, DateTimeOffset.UtcNow, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results[MediaAnalysis.Describe] = new ModeStatus(false, null, ex.Message);
            }
        }

        if (metadata.Analysis.HasFlag(MediaAnalysis.Ocr))
        {
            try
            {
                var text = await Client.Ocr(content, ct);
                SetProperty(entity, metadata.OcrTextProperty, text);
                results[MediaAnalysis.Ocr] = new ModeStatus(true, DateTimeOffset.UtcNow, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results[MediaAnalysis.Ocr] = new ModeStatus(false, null, ex.Message);
            }
        }

        if (metadata.Analysis.HasFlag(MediaAnalysis.Transcribe))
        {
            // Transcription not yet available in Client — mark as skipped
            results[MediaAnalysis.Transcribe] = new ModeStatus(false, null, "Transcription not yet implemented");
        }

        if (metadata.Analysis.HasFlag(MediaAnalysis.Classify))
        {
            try
            {
                var classification = await Client.Chat(
                    "Classify this image into a single category. Return only the category name.",
                    new ChatOptions { Image = content },
                    ct);
                SetProperty(entity, metadata.ClassifyProperty, classification?.Trim());
                results[MediaAnalysis.Classify] = new ModeStatus(true, DateTimeOffset.UtcNow, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results[MediaAnalysis.Classify] = new ModeStatus(false, null, ex.Message);
            }
        }

        if (metadata.Analysis.HasFlag(MediaAnalysis.Extract))
        {
            try
            {
                var prompt = metadata.PromptName ?? "Extract structured data from this image as JSON.";
                var extracted = await Client.Chat(
                    prompt,
                    new ChatOptions { Image = content },
                    ct);
                SetProperty(entity, metadata.ExtractedDataProperty, extracted);
                results[MediaAnalysis.Extract] = new ModeStatus(true, DateTimeOffset.UtcNow, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results[MediaAnalysis.Extract] = new ModeStatus(false, null, ex.Message);
            }
        }

        return results;
    }

    private static void SetProperty<TEntity>(TEntity entity, string? propertyName, object? value)
    {
        if (propertyName is null || value is null) return;
        var prop = typeof(TEntity).GetProperty(propertyName);
        if (prop?.CanWrite == true)
            prop.SetValue(entity, value);
    }
}
