using System;
using Koan.AI.Pipelines;

namespace Koan.AI;

/// <summary>
/// Fluent pipeline API for AI transformations.
/// Provides elegant chaining of AI operations: text → image → storage, etc.
/// </summary>
/// <example>
/// <code>
/// // Text to image to storage
/// var result = await Ai.FromText("A majestic mountain at sunset")
///     .ToImage(model: "dall-e-3")
///     .ToStorage(container: "generated-images");
///
/// // Text to embedding
/// var embedding = await Ai.FromText("Machine learning in healthcare")
///     .ToEmbedding(model: "text-embedding-3-large");
///
/// // Image to text analysis
/// var description = await Ai.FromImage(photoBytes)
///     .ToText("Describe this photo in detail", model: "gpt-4o");
///
/// // Streaming chat
/// await foreach (var chunk in Ai.FromText("Explain quantum computing").Stream())
/// {
///     Console.Write(chunk);
/// }
/// </code>
/// </example>
public static class Ai
{
    /// <summary>
    /// Start a text-based AI pipeline.
    /// </summary>
    /// <param name="text">Input text for transformation</param>
    /// <returns>Text pipeline stage</returns>
    /// <example>
    /// <code>
    /// var embedding = await Ai.FromText("Hello world").ToEmbedding();
    /// var image = await Ai.FromText("A sunset").ToImage().ToBytes();
    /// </code>
    /// </example>
    public static TextPipeline FromText(string text)
        => new TextPipeline(text, PipelineContext.Current);

    /// <summary>
    /// Start an image-based AI pipeline.
    /// </summary>
    /// <param name="bytes">Image data</param>
    /// <param name="mimeType">MIME type (default: image/jpeg)</param>
    /// <returns>Image pipeline stage</returns>
    /// <example>
    /// <code>
    /// var description = await Ai.FromImage(photoBytes)
    ///     .ToText("What's in this image?");
    ///
    /// var result = await Ai.FromImage(photoBytes)
    ///     .ToStorage(container: "photos");
    /// </code>
    /// </example>
    public static ImagePipeline FromImage(byte[] bytes, string? mimeType = "image/jpeg")
        => new ImagePipeline(bytes, mimeType, PipelineContext.Current);

    /// <summary>
    /// Create a scoped context for AI operations with source/provider/model overrides.
    /// Context flows through pipeline operations.
    /// </summary>
    /// <param name="source">Source or group name (e.g., "ollama-primary", "openai-prod")</param>
    /// <param name="provider">Provider type (e.g., "ollama", "openai")</param>
    /// <param name="model">Model name (e.g., "llama3.2:70b", "gpt-4o")</param>
    /// <returns>Disposable scope that restores previous context when disposed</returns>
    /// <example>
    /// <code>
    /// using (Ai.Context(source: "openai-prod"))
    /// {
    ///     var result = await Ai.FromText("Generate product image")
    ///         .ToImage()
    ///         .ToStorage();
    /// }
    /// </code>
    /// </example>
    public static IDisposable Context(string? source = null, string? provider = null, string? model = null)
        => Client.Context(source, provider, model);
}
