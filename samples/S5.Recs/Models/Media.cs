using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Data.Vector.Abstractions;
using S5.Recs.Infrastructure;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[VectorAdapter("weaviate")]
[Storage(Name = "Media")]
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

    // External references and metadata
    public Dictionary<string, string> ExternalIds { get; set; } = new();
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

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
}