using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Relationships;
using Koan.Data.Vector.Abstractions;
using S5.Recs.Infrastructure;

namespace S5.Recs.Models;

[Storage(Name = "Media")]
[OptimizeStorage(OptimizationType = StorageOptimizationType.None, Reason = "Uses SHA512-based deterministic string IDs, not GUIDs")]
public sealed class Media : Entity<Media>
{
    [Parent(typeof(MediaType))]
    public required string MediaTypeId { get; set; }

    [Parent(typeof(MediaFormat))]
    public required string MediaFormatId { get; set; }

    // Source identification for deterministic ID generation
    public required string ProviderCode { get; set; }  // "anilist", "mal"
    public required string ExternalId { get; set; }   // Provider's native ID

    public required string Title { get; set; }
    public string? TitleEnglish { get; set; }
    public string? TitleRomaji { get; set; }
    public string? TitleNative { get; set; }
    public string[] Synonyms { get; set; } = Array.Empty<string>();

    public string[] Genres { get; set; } = Array.Empty<string>();
    public string[] Tags { get; set; } = Array.Empty<string>();

    // Media-specific metrics (null for non-applicable types)
    public int? Episodes { get; set; }     // For series/episodic content
    public int? Chapters { get; set; }     // For manga
    public int? Volumes { get; set; }      // For manga
    public int? Duration { get; set; }     // For movies (minutes)

    public string? Synopsis { get; set; }
    public double Popularity { get; set; }
    public double? AverageScore { get; set; }

    // Visual assets
    public string? CoverUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? CoverColorHex { get; set; }

    // Publication/release info
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// Numeric representation of StartDate in YYYYMMDD format for efficient filtering.
    /// Example: 2023-12-31 becomes 20231231
    /// </summary>
    public int? StartDateInt => StartDate.HasValue
        ? StartDate.Value.Year * 10000 + StartDate.Value.Month * 100 + StartDate.Value.Day
        : null;

    /// <summary>
    /// Numeric representation of EndDate in YYYYMMDD format for efficient filtering.
    /// Example: 2023-12-31 becomes 20231231
    /// </summary>
    public int? EndDateInt => EndDate.HasValue
        ? EndDate.Value.Year * 10000 + EndDate.Value.Month * 100 + EndDate.Value.Day
        : null;

    /// <summary>
    /// Stored rating value for efficient filtering (same as Rating computed property).
    /// Blended rating: 80% AverageScore + 20% Popularity
    /// </summary>
    public double? RatingValue => AverageScore.HasValue
        ? Math.Round((AverageScore.Value * 0.8) + Popularity, 2)
        : null;

    // External references and metadata
    public Dictionary<string, string> ExternalIds { get; set; } = new();
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // ─────────────────────────────────────────────────────────────────
    // Pipeline Metadata (ARCH-0069: Partition-Based Import Pipeline)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ID of the import job that imported this media item
    /// </summary>
    public string? ImportJobId { get; set; }

    /// <summary>
    /// SHA256 content signature for embedding cache lookup.
    /// Computed from: Titles + Synopsis + Genres + Tags
    /// </summary>
    public string? ContentSignature { get; set; }

    /// <summary>
    /// When this media item passed validation and entered vectorization queue
    /// </summary>
    public DateTimeOffset? ValidatedAt { get; set; }

    /// <summary>
    /// When embedding was generated and stored in vector database
    /// </summary>
    public DateTimeOffset? VectorizedAt { get; set; }

    /// <summary>
    /// Error message from last processing attempt (validation or vectorization)
    /// </summary>
    public string? ProcessingError { get; set; }

    /// <summary>
    /// Number of times processing has been retried for this media item
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Generates deterministic SHA512-based ID from provider information.
    /// </summary>
    /// <param name="providerCode">Provider code (e.g., "anilist", "mal")</param>
    /// <param name="externalId">Provider's native ID</param>
    /// <param name="mediaTypeId">Media type ID</param>
    /// <returns>Deterministic SHA512-based ID</returns>
    public static string MakeId(string providerCode, string externalId, string mediaTypeId)
    {
        return IdGenerationUtilities.GenerateMediaId(providerCode, externalId, mediaTypeId);
    }

    // ID generation handled by MakeId method when needed

    /// <summary>
    /// Computed year from StartDate for filtering and display
    /// </summary>
    public int? Year => StartDate?.Year;

    /// <summary>
    /// Blended rating: 80% AverageScore + 20% Popularity (scaled to 5★)
    /// Formula: (AverageScore × 0.8) + (Popularity × 1)
    /// Range: ~3.5-4.6★ for typical content
    /// </summary>
    public double? Rating => AverageScore.HasValue
        ? Math.Round((AverageScore.Value * 0.8) + Popularity, 2)
        : null;
}