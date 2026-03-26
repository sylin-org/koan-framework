using System.Reflection;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.AI.Attributes;
using Microsoft.Extensions.Logging;

namespace Koan.Data.AI;

/// <summary>
/// Entity-aware AI operations. Bridges entity content extraction (EmbeddingMetadata)
/// with AI operations (Client). Lives in Koan.Data.AI because it depends on both layers.
///
/// Convention-first: works without [Embedding] attribute for on-demand operations.
/// [Embedding] attribute gates auto-embed-on-save lifecycle, not these methods.
/// </summary>
public static class EntityAi
{
    private static readonly ILogger? _logger =
        (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(ILoggerFactory)) as ILoggerFactory)
            ?.CreateLogger("Koan.Data.AI.EntityAi");

    // ========================================================================
    // Embed — entity convention inference
    // ========================================================================

    /// <summary>
    /// Generate an embedding vector from an entity's content.
    /// Content extraction follows EmbeddingMetadata convention chain:
    /// [Embedding] attribute → AllStrings convention → JSON fallback.
    /// Source routing is applied from metadata if configured.
    /// </summary>
    public static async Task<float[]> Embed<TEntity>(TEntity entity, CancellationToken ct = default)
        where TEntity : class
    {
        var (text, metadata) = ExtractEmbeddingContent(entity);
        if (string.IsNullOrWhiteSpace(text))
        {
            LogEmptyContent<TEntity>("Embed");
            return [];
        }

        using (metadata.Source is not null ? Client.Scope(embed: metadata.Source) : null)
        {
            var options = metadata.Model is not null ? new EmbedOptions { Model = metadata.Model } : null;
            return options is not null
                ? await Client.Embed(text, options, ct)
                : await Client.Embed(text, ct);
        }
    }

    /// <summary>
    /// Generate an embedding from an entity and return a rich result with metadata.
    /// </summary>
    public static async Task<Koan.AI.Contracts.Models.EmbedResult> EmbedResult<TEntity>(
        TEntity entity, CancellationToken ct = default)
        where TEntity : class
    {
        var (text, metadata) = ExtractEmbeddingContent(entity);
        if (string.IsNullOrWhiteSpace(text))
        {
            LogEmptyContent<TEntity>("EmbedResult");
            return new Koan.AI.Contracts.Models.EmbedResult { Vector = [], Dimension = 0 };
        }

        using (metadata.Source is not null ? Client.Scope(embed: metadata.Source) : null)
        {
            return await Client.EmbedResult(text, ct);
        }
    }

    // ========================================================================
    // Chat — entity as context
    // ========================================================================

    /// <summary>
    /// Chat with AI, injecting entity content as context.
    /// Entity is serialized using EmbeddingMetadata convention and prepended
    /// as a system-level context block.
    /// </summary>
    public static async Task<string> Chat<TEntity>(
        string message, TEntity entity, CancellationToken ct = default)
        where TEntity : class
    {
        var options = BuildEntityContextOptions(entity);
        return await Client.Chat(message, options, ct);
    }

    /// <summary>
    /// Chat with AI using entity context and additional options.
    /// Entity context is merged with provided options (entity context prepends any existing system prompt).
    /// </summary>
    public static async Task<string> Chat<TEntity>(
        string message, TEntity entity, ChatOptions options, CancellationToken ct = default)
        where TEntity : class
    {
        var merged = MergeEntityContext(entity, options);
        return await Client.Chat(message, merged, ct);
    }

    /// <summary>
    /// Chat with entity context and return a rich result with metadata.
    /// </summary>
    public static async Task<Koan.AI.Contracts.Models.ChatResult> ChatResult<TEntity>(
        string message, TEntity entity, CancellationToken ct = default)
        where TEntity : class
    {
        var options = BuildEntityContextOptions(entity);
        return await Client.ChatResult(message, options, ct);
    }

    // ========================================================================
    // OCR — entity byte[] extraction
    // ========================================================================

    /// <summary>
    /// Extract text from an entity's binary content using OCR.
    /// Scans for the first byte[] property by convention:
    /// [MediaAnalysis] attribute → convention names (Data, Content, ImageData, Bytes, FileData).
    /// </summary>
    public static async Task<string> Ocr<TEntity>(TEntity entity, CancellationToken ct = default)
        where TEntity : class
    {
        var bytes = ExtractBytes(entity);
        if (bytes is null || bytes.Length == 0)
        {
            _logger?.LogWarning(
                "No binary content found on {EntityType} for OCR. " +
                "Ensure the entity has a byte[] property (Data, Content, ImageData, Bytes, or FileData).",
                typeof(TEntity).Name);
            return "";
        }

        return await Client.Ocr(bytes, ct);
    }

    /// <summary>
    /// Extract text from an entity's binary content with OCR options.
    /// </summary>
    public static async Task<string> Ocr<TEntity>(
        TEntity entity, OcrOptions options, CancellationToken ct = default)
        where TEntity : class
    {
        var bytes = ExtractBytes(entity);
        if (bytes is null || bytes.Length == 0)
        {
            _logger?.LogWarning(
                "No binary content found on {EntityType} for OCR.",
                typeof(TEntity).Name);
            return "";
        }

        return await Client.Ocr(bytes, options, ct);
    }

    /// <summary>
    /// OCR with entity byte[] extraction, returning a rich result.
    /// </summary>
    public static async Task<Koan.AI.Contracts.Models.OcrResult> OcrResult<TEntity>(
        TEntity entity, CancellationToken ct = default)
        where TEntity : class
    {
        var bytes = ExtractBytes(entity);
        if (bytes is null || bytes.Length == 0)
        {
            _logger?.LogWarning(
                "No binary content found on {EntityType} for OCR.",
                typeof(TEntity).Name);
            return new Koan.AI.Contracts.Models.OcrResult { Text = "" };
        }

        return await Client.OcrResult(bytes, ct);
    }

    // ========================================================================
    // Content extraction helpers
    // ========================================================================

    /// <summary>
    /// Extract embedding text from an entity using convention inference.
    /// Useful when you need the raw text without generating an embedding.
    /// </summary>
    public static string ExtractText<TEntity>(TEntity entity) where TEntity : class
    {
        var metadata = EmbeddingMetadata.Resolve<TEntity>();
        return metadata.BuildEmbeddingText(entity);
    }

    // ========================================================================
    // Internal
    // ========================================================================

    private static (string text, EmbeddingMetadata metadata) ExtractEmbeddingContent<TEntity>(TEntity entity)
        where TEntity : class
    {
        var metadata = EmbeddingMetadata.Resolve<TEntity>();
        var text = metadata.BuildEmbeddingText(entity);
        return (text, metadata);
    }

    private static ChatOptions BuildEntityContextOptions<TEntity>(TEntity entity)
        where TEntity : class
    {
        var context = BuildEntityContext(entity);
        return new ChatOptions { SystemPrompt = context };
    }

    private static ChatOptions MergeEntityContext<TEntity>(TEntity entity, ChatOptions existing)
        where TEntity : class
    {
        var context = BuildEntityContext(entity);
        var existingSystem = existing.SystemPrompt;

        var merged = string.IsNullOrWhiteSpace(existingSystem)
            ? context
            : $"{context}\n\n{existingSystem}";

        return existing with { SystemPrompt = merged };
    }

    private static string BuildEntityContext<TEntity>(TEntity entity) where TEntity : class
    {
        var metadata = EmbeddingMetadata.Resolve<TEntity>();
        var content = metadata.BuildEmbeddingText(entity);
        var typeName = typeof(TEntity).Name;

        return string.IsNullOrWhiteSpace(content)
            ? $"Context: {typeName} (no extractable content)"
            : $"Context ({typeName}):\n{content}";
    }

    private static readonly string[] BytePropertyConventions =
        ["Data", "Content", "ImageData", "Bytes", "FileData", "RawBytes", "Binary"];

    private static byte[]? ExtractBytes<TEntity>(TEntity entity) where TEntity : class
    {
        var entityType = typeof(TEntity);

        // Check [MediaAnalysis] attribute for explicit property mapping first
        var mediaMetadata = MediaAnalysisMetadata.Resolve<TEntity>();
        if (mediaMetadata is not null)
        {
            // MediaAnalysis entities typically have a known data property
            var dataProp = entityType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.PropertyType == typeof(byte[]) && p.CanRead);

            if (dataProp is not null)
                return dataProp.GetValue(entity) as byte[];
        }

        // Convention: scan for first byte[] property matching known names
        foreach (var name in BytePropertyConventions)
        {
            var prop = entityType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is not null && prop.PropertyType == typeof(byte[]) && prop.CanRead)
                return prop.GetValue(entity) as byte[];
        }

        // Fallback: first byte[] property
        var firstBytes = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.PropertyType == typeof(byte[]) && p.CanRead);

        return firstBytes?.GetValue(entity) as byte[];
    }

    private static void LogEmptyContent<TEntity>(string operation)
    {
        _logger?.LogWarning(
            "No embeddable content found on {EntityType} for {Operation}. " +
            "Add [Embedding] to configure auto-embed-on-save, or ensure the entity has string properties. " +
            "Convention: all public string properties (excluding Id) are used by default.",
            typeof(TEntity).Name, operation);
    }
}
