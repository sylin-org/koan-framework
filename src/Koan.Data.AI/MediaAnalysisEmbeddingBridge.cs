using System.Text;

namespace Koan.Data.AI;

/// <summary>
/// Composes text from media analysis results for embedding generation.
/// When an entity has both [MediaAnalysis] and [Embedding], analysis output
/// (description, OCR text, transcript, classification, extracted data)
/// feeds into the embedding text in a deterministic order.
///
/// Order: Describe → Ocr → Transcript → Classify → Extract
/// Null/empty properties are skipped.
/// </summary>
public static class MediaAnalysisEmbeddingBridge
{
    /// <summary>
    /// Composes embedding-ready text from the entity's media analysis result properties.
    /// Returns empty string if no analysis properties are populated.
    /// </summary>
    public static string ComposeText<TEntity>(TEntity entity) where TEntity : class
    {
        var metadata = MediaAnalysisMetadata.Resolve<TEntity>();
        if (metadata is null) return string.Empty;

        return ComposeText(entity, metadata);
    }

    /// <summary>
    /// Composes embedding-ready text from the entity's media analysis result properties
    /// using pre-resolved metadata.
    /// </summary>
    public static string ComposeText<TEntity>(TEntity entity, MediaAnalysisMetadata metadata) where TEntity : class
    {
        var entityType = typeof(TEntity);
        var sb = new StringBuilder();

        // Deterministic order: Describe → Ocr → Transcript → Classify → Extract
        AppendPropertyValue(sb, entity, entityType, metadata.DescriptionProperty, "Description");
        AppendPropertyValue(sb, entity, entityType, metadata.OcrTextProperty, "OCR");
        AppendPropertyValue(sb, entity, entityType, metadata.TranscriptProperty, "Transcript");
        AppendPropertyValue(sb, entity, entityType, metadata.ClassifyProperty, "Classification");
        AppendPropertyValue(sb, entity, entityType, metadata.ExtractedDataProperty, "Extracted");

        return sb.ToString().Trim();
    }

    private static void AppendPropertyValue<TEntity>(
        StringBuilder sb, TEntity entity, Type entityType, string? propertyName, string label)
        where TEntity : class
    {
        if (propertyName is null) return;

        var prop = entityType.GetProperty(propertyName);
        if (prop is null) return;

        var value = prop.GetValue(entity);
        var text = value?.ToString();

        if (string.IsNullOrWhiteSpace(text)) return;

        if (sb.Length > 0) sb.AppendLine();
        sb.Append(label).Append(": ").Append(text);
    }
}
