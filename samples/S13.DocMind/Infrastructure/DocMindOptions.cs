using System;
using System.ComponentModel.DataAnnotations;

namespace S13.DocMind.Infrastructure;

public sealed class DocMindOptions
{
    public const string Section = "DocMind";

    [Required]
    public StorageOptions Storage { get; set; } = new();

    [Required]
    public ProcessingOptions Processing { get; set; } = new();

    [Required]
    public AiOptions Ai { get; set; } = new();

    public sealed class StorageOptions
    {
        [Required]
        public string BasePath { get; set; } = "uploads";

        [Range(1, long.MaxValue)]
        public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

        [Required]
        public string[] AllowedContentTypes { get; set; } =
        {
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "text/plain",
            "image/png",
            "image/jpeg"
        };
    }

    public sealed class ProcessingOptions
    {
        [Range(1, int.MaxValue)]
        public int QueueCapacity { get; set; } = 64;

        [Range(1, int.MaxValue)]
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

        [Range(128, 4096)]
        public int ChunkSizeTokens { get; set; } = 800;

        public bool EnableVisionExtraction { get; set; } = true;
    }

    public sealed class AiOptions
    {
        [Required]
        public string DefaultModel { get; set; } = "llama3";

        [Required]
        public string EmbeddingModel { get; set; } = "nomic-embed-text";

        public string? VisionModel { get; set; } = "llava";
    }
}
