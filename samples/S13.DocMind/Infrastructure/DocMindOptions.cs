using System.ComponentModel.DataAnnotations;

namespace S13.DocMind.Infrastructure;

public sealed class DocMindStorageOptions
{
    public const string Section = "DocMind:Storage";

    [Required]
    public string BasePath { get; set; } = "uploads";

    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    public string[] AllowedContentTypes { get; set; } =
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain",
        "image/png",
        "image/jpeg"
    };
}

public sealed class DocMindProcessingOptions
{
    public const string Section = "DocMind:Processing";

    public int QueueCapacity { get; set; } = 64;
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public int ChunkSizeTokens { get; set; } = 800;
    public bool EnableVisionExtraction { get; set; } = true;
}

public sealed class DocMindAiOptions
{
    public const string Section = "DocMind:Ai";

    public string DefaultModel { get; set; } = "llama3";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string? VisionModel { get; set; } = "llava";
}
