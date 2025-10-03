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

    [Required]
    public ManualAnalysisOptions Manual { get; set; } = new();

    public sealed class StorageOptions
    {
        [Required]
        public string BasePath { get; set; } = "uploads";

        [Required]
        [StringLength(120)]
        public string Bucket { get; set; } = "local";

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
        [Range(4, 1024)]
        public int QueueCapacity { get; set; } = 48;

        [Range(1, 64)]
        public int MaxConcurrency { get; set; } = Math.Clamp(Environment.ProcessorCount, 1, 16);

        [Range(200, 2000)]
        public int ChunkSizeTokens { get; set; } = 600;

        public bool EnableVisionExtraction { get; set; } = true;

        [Range(1, 64)]
        public int WorkerBatchSize { get; set; } = 4;

        [Range(1, 20)]
        public int MaxRetryAttempts { get; set; } = 5;

        [Range(1, 3600)]
        public int RetryInitialDelaySeconds { get; set; } = 5;

        [Range(1, 3600)]
        public int RetryMaxDelaySeconds { get; set; } = 300;

        [Range(1.0, 10.0)]
        public double RetryBackoffMultiplier { get; set; } = 2.0;

        public bool RetryUseJitter { get; set; } = true;

        [Range(1, 120)]
        public int PollIntervalSeconds { get; set; } = 5;
    }

    public sealed class AiOptions
    {
        [Required]
        public string DefaultModel { get; set; } = "llama3";

        [Required]
        public string EmbeddingModel { get; set; } = "nomic-embed-text";

        public string? VisionModel { get; set; } = "llava";
    }

    public sealed class ManualAnalysisOptions
    {
        [Range(2, 50)]
        public int MaxDocuments { get; set; } = 10;

        [Range(1, 20000)]
        public int MaxPromptTokens { get; set; } = 8000;

        public bool EnableSessions { get; set; } = true;

        [Range(0.0, 1.0)]
        public double DefaultConfidence { get; set; } = 0.85;
    }
}
