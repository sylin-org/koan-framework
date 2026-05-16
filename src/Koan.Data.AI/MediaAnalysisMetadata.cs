using System.Collections.Concurrent;
using System.Reflection;
using Koan.Data.AI.Attributes;

namespace Koan.Data.AI;

/// <summary>
/// Runtime metadata cache for media analysis configuration.
/// Resolves [MediaAnalysis] attribute + convention-detected property mappings.
/// Returns null for types without the attribute (attribute = lifecycle opt-in).
/// Cached per entity type — no per-request reflection cost.
/// </summary>
public sealed class MediaAnalysisMetadata
{
    private static readonly ConcurrentDictionary<Type, MediaAnalysisMetadata?> _cache = new();

    /// <summary>Which analysis modes to perform.</summary>
    public MediaAnalysis Analysis { get; init; }

    /// <summary>Resolved property name for Describe output (vision description).</summary>
    public string? DescriptionProperty { get; init; }

    /// <summary>Resolved property name for OCR text output.</summary>
    public string? OcrTextProperty { get; init; }

    /// <summary>Resolved property name for Transcribe output.</summary>
    public string? TranscriptProperty { get; init; }

    /// <summary>Resolved property name for Classify output.</summary>
    public string? ClassifyProperty { get; init; }

    /// <summary>Resolved property name for structured extraction output.</summary>
    public string? ExtractedDataProperty { get; init; }

    /// <summary>Process asynchronously (default from attribute).</summary>
    public bool Async { get; init; }

    /// <summary>Named prompt from PromptEntry catalog.</summary>
    public string? PromptName { get; init; }

    /// <summary>Schema version — increment triggers re-analysis.</summary>
    public int Version { get; init; }

    /// <summary>
    /// Resolves analysis metadata for the specified entity type.
    /// Returns null if no [MediaAnalysis] attribute is present.
    /// </summary>
    public static MediaAnalysisMetadata? Resolve<TEntity>() where TEntity : class
    {
        return Resolve(typeof(TEntity));
    }

    /// <summary>
    /// Resolves analysis metadata for the specified entity type.
    /// Returns null if no [MediaAnalysis] attribute is present.
    /// </summary>
    public static MediaAnalysisMetadata? Resolve(Type entityType)
    {
        return _cache.GetOrAdd(entityType, BuildMetadata);
    }

    private static MediaAnalysisMetadata? BuildMetadata(Type entityType)
    {
        var attr = entityType.GetCustomAttribute<MediaAnalysisAttribute>();
        if (attr is null) return null;

        var props = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();

        return new MediaAnalysisMetadata
        {
            Analysis = attr.Analysis,
            DescriptionProperty = ResolveProperty(
                attr.DescriptionProperty, props, typeof(string),
                "AiDescription", "Description", "Summary"),
            OcrTextProperty = ResolveProperty(
                attr.OcrTextProperty, props, typeof(string),
                "OcrText", "ExtractedText", "Text"),
            TranscriptProperty = ResolveProperty(
                attr.TranscriptProperty, props, typeof(string),
                "Transcript", "Transcription"),
            ClassifyProperty = ResolveProperty(
                attr.ClassificationProperty, props, null,
                "Category", "Classification", "MediaType"),
            ExtractedDataProperty = ResolveProperty(
                attr.ExtractedDataProperty, props, null,
                "ExtractedData", "Terms", "Analysis"),
            Async = attr.Async,
            PromptName = attr.Prompt,
            Version = attr.Version,
        };
    }

    /// <summary>
    /// Resolves a target property by explicit name or convention detection.
    /// When expectedType is null, any writable property matching a convention name is accepted
    /// (used for Classify where string or enum types are both valid).
    /// </summary>
    private static string? ResolveProperty(
        string? explicitName,
        List<PropertyInfo> props,
        Type? expectedType,
        params string[] conventionNames)
    {
        // Explicit mapping takes priority
        if (explicitName is not null)
        {
            var explicitProp = props.FirstOrDefault(p =>
                string.Equals(p.Name, explicitName, StringComparison.OrdinalIgnoreCase));
            return explicitProp?.Name;
        }

        // Convention detection: first matching property name with compatible type
        foreach (var name in conventionNames)
        {
            var prop = props.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (prop is null) continue;

            // Accept any writable property when expectedType is null (flexible modes like Classify)
            if (expectedType is null || prop.PropertyType == expectedType ||
                Nullable.GetUnderlyingType(prop.PropertyType) == expectedType)
            {
                return prop.Name;
            }
        }

        return null;
    }
}
