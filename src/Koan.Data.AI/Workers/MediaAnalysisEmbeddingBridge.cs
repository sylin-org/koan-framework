namespace Koan.Data.AI.Workers;

/// <summary>
/// Concatenates media analysis results into a single embedding text string.
/// Used to bridge [MediaAnalysis] output into [Embedding] input —
/// analysis results feed into embedding generation automatically.
///
/// Property values are joined in deterministic order:
/// Describe -> Ocr -> Transcript -> Classify -> Extract.
/// </summary>
internal static class MediaAnalysisEmbeddingBridge
{
    /// <summary>
    /// Composes embedding text from analysis result properties on the entity.
    /// Returns empty string if no analysis properties contain values.
    /// </summary>
    public static string ComposeEmbeddingText<TEntity>(TEntity entity, MediaAnalysisMetadata metadata)
        where TEntity : class
    {
        var parts = new List<string>();
        var entityType = typeof(TEntity);

        // Deterministic order: Describe -> Ocr -> Transcript -> Classify -> Extract
        AppendProperty(parts, entity, entityType, metadata.DescriptionProperty);
        AppendProperty(parts, entity, entityType, metadata.OcrTextProperty);
        AppendProperty(parts, entity, entityType, metadata.TranscriptProperty);
        AppendProperty(parts, entity, entityType, metadata.ClassifyProperty);
        AppendProperty(parts, entity, entityType, metadata.ExtractedDataProperty);

        return string.Join("\n\n", parts);
    }

    private static void AppendProperty(List<string> parts, object entity, Type type, string? propName)
    {
        if (propName is null) return;
        var prop = type.GetProperty(propName);
        var value = prop?.GetValue(entity)?.ToString();
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add(value);
    }
}
