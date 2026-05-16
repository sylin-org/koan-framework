using Koan.AI.Contracts.Models;

namespace Koan.Rag.Abstractions;

/// <summary>
/// Converts raw file content (PDF, image, audio, etc.) into rich text
/// suitable for RAG ingestion. Adapters are auto-discovered and registered
/// via <c>ContentAdapterAttribute</c> or DI.
/// <para>
/// The multi-round protocol: classify → interpret → enrich.
/// Pre-determined strategies handle known content types; auto-generated
/// strategies handle novel content using the best available reasoning model.
/// </para>
/// </summary>
public interface IContentAdapter
{
    /// <summary>Unique adapter identifier.</summary>
    string Id { get; }

    /// <summary>File extensions this adapter handles (e.g., ".pdf", ".png").</summary>
    IReadOnlySet<string> SupportedExtensions { get; }

    /// <summary>Modalities this adapter processes.</summary>
    IReadOnlySet<Modality> SupportedModalities { get; }

    /// <summary>
    /// Can this adapter handle the given file? Enables fine-grained routing
    /// beyond extension matching (e.g., PDF with scanned pages vs text-only PDF).
    /// </summary>
    bool CanProcess(ContentExtractionRequest request);

    /// <summary>
    /// Extract rich text from the content using the multi-round protocol.
    /// </summary>
    Task<ContentExtractionResult> Extract(
        ContentExtractionRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Marks a class as a content adapter and declares its supported extensions.
/// Discovered by <c>ContentAdapterRegistry</c> during auto-registration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ContentAdapterAttribute : Attribute
{
    public ContentAdapterAttribute(params string[] extensions)
    {
        Extensions = extensions;
    }

    /// <summary>Supported file extensions (e.g., ".pdf", ".png", ".mp3").</summary>
    public IReadOnlyList<string> Extensions { get; }
}
